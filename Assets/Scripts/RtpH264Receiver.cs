using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace VrTeleop
{
    /// <summary>
    /// UDP(RTP/H.264) 수신 + 디페이로타이징(RFC 6184).
    ///  - RTP 헤더(12B + CSRC/확장) 제거
    ///  - 단일 NAL / STAP-A(24) / FU-A(28) 재조립 -> Annex-B(00 00 00 01) access unit
    ///  - marker 비트로 프레임 경계 판정 후 큐에 push
    ///  - SPS(7)/PPS(8) 수집 -> 디코더 초기화용 (config-interval=1이라 곧 채워짐)
    ///
    /// 네트워크 스레드에서 파싱하고 결과만 ConcurrentQueue로 넘긴다.
    /// RTP 시퀀스 갭(손실/재정렬) 감지 시, 개별 프레임을 버리는 게 아니라 "다음 IDR까지
    /// 스킵"한다. P-프레임을 함부로 버리면 참조 체인이 끊겨 깨짐이 다음 키프레임까지 번지므로,
    /// 손실 후 깨끗한 IDR부터 재개해 참조를 리셋한다(짧은 GOP일수록 복구가 빠름).
    /// </summary>
    public class RtpH264Receiver : MonoBehaviour
    {
        [Tooltip("Jetson udpsink 이 쏘는 포트")]
        public int port = 5600;

        [Tooltip("큐가 이보다 커지면 오래된 프레임을 버려 지연 누적 방지")]
        public int maxQueued = 3;

        [Tooltip("ON: 손실 시 다음 IDR까지 스킵(깨짐 방지, 대신 끊김↑). " +
                 "OFF: 손실 프레임도 그대로 공급(30fps 유지·부드럽지만 손실 순간 약간 깨짐). teleop은 OFF가 나을 수 있음")]
        public bool waitForIdrOnLoss = true;

        public struct AccessUnit
        {
            public byte[] data;   // start code 포함 Annex-B
            public long ptsUs;    // presentation timestamp (us)
        }

        public readonly ConcurrentQueue<AccessUnit> Frames = new ConcurrentQueue<AccessUnit>();

        public byte[] Sps { get; private set; }   // start code 포함
        public byte[] Pps { get; private set; }   // start code 포함
        public bool HasParameterSets => Sps != null && Pps != null;

        // SPS 에서 파싱한 실제 영상 해상도 (확정 전 0). 영상 크기 변경에 유동 대응.
        public int VideoWidth { get; private set; }
        public int VideoHeight { get; private set; }

        static readonly byte[] StartCode = { 0x00, 0x00, 0x00, 0x01 };

        UdpClient _udp;
        Thread _thread;
        volatile bool _running;

        // 현재 조립 중인 access unit / FU-A 조각 버퍼
        readonly System.Collections.Generic.List<byte> _au = new System.Collections.Generic.List<byte>(256 * 1024);
        readonly System.Collections.Generic.List<byte> _fu = new System.Collections.Generic.List<byte>(256 * 1024);
        bool _fuActive;

        // RTP 시퀀스 추적 + 손실 복구 상태
        int _expectedSeq = -1;
        bool _auCorrupt;             // 현재 프레임 도중 시퀀스 갭(손실/재정렬) 발생
        bool _auHasIdr;              // 현재 프레임에 IDR(키프레임) 슬라이스 포함
        bool _needKeyframe = true;   // 손실 후 다음 IDR까지 프레임 스킵(참조 체인 리셋)

        // 진단 통계(초당): 실제 전달 fps / 손실로 버린 프레임 / IDR 대기 스킵
        int _statDelivered, _statLoss, _statWait;
        float _statTimer;

        void OnEnable()
        {
            _udp = new UdpClient(port);
            _udp.Client.ReceiveBufferSize = 1 << 21; // 2MB: 버스트 유실 방지
            _running = true;
            _thread = new Thread(RxLoop) { IsBackground = true, Name = "RtpH264Rx" };
            _thread.Start();
            Debug.Log($"[Rtp] listening udp:{port}");
        }

        void OnDisable()
        {
            _running = false;
            try { _udp?.Close(); } catch { }
            try { _thread?.Join(200); } catch { }
            _udp = null;
            _thread = null;
        }

        void RxLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, port);
            while (_running)
            {
                byte[] pkt;
                try { pkt = _udp.Receive(ref any); }
                catch (SocketException) { break; }
                catch (ObjectDisposedException) { break; }
                if (pkt == null || pkt.Length < 12) continue;
                try { HandlePacket(pkt); }
                catch (Exception e) { Debug.LogWarning($"[Rtp] parse error: {e.Message}"); }
            }
        }

        void HandlePacket(byte[] p)
        {
            int cc = p[0] & 0x0F;
            bool ext = (p[0] & 0x10) != 0;
            bool marker = (p[1] & 0x80) != 0;

            // 시퀀스 갭 감지 -> 현재 프레임 손상 표시 (오프셋 2~3은 cc/ext 와 무관하게 고정)
            int seq = (p[2] << 8) | p[3];
            if (_expectedSeq >= 0 && seq != _expectedSeq)
                _auCorrupt = true;
            _expectedSeq = (seq + 1) & 0xFFFF;

            int hdr = 12 + cc * 4;
            if (ext)
            {
                if (p.Length < hdr + 4) return;
                int extWords = (p[hdr + 2] << 8) | p[hdr + 3];
                hdr += 4 + extWords * 4;
            }
            if (p.Length <= hdr) return;

            uint rtpTs = (uint)((p[4] << 24) | (p[5] << 16) | (p[6] << 8) | p[7]);
            long ptsUs = (long)rtpTs * 1000 / 90; // 90kHz -> us

            int type = p[hdr] & 0x1F;

            if (type >= 1 && type <= 23)
            {
                EmitNal(p, hdr, p.Length - hdr);
            }
            else if (type == 24) // STAP-A
            {
                int o = hdr + 1;
                while (o + 2 <= p.Length)
                {
                    int sz = (p[o] << 8) | p[o + 1];
                    o += 2;
                    if (sz <= 0 || o + sz > p.Length) break;
                    EmitNal(p, o, sz);
                    o += sz;
                }
            }
            else if (type == 28) // FU-A
            {
                byte fuInd = p[hdr];
                byte fuHdr = p[hdr + 1];
                bool start = (fuHdr & 0x80) != 0;
                bool end = (fuHdr & 0x40) != 0;
                byte nalHeader = (byte)((fuInd & 0xE0) | (fuHdr & 0x1F));

                if (start)
                {
                    _fu.Clear();
                    _fu.Add(nalHeader);
                    _fuActive = true;
                }
                if (_fuActive)
                {
                    for (int i = hdr + 2; i < p.Length; i++) _fu.Add(p[i]);
                    if (end)
                    {
                        var nal = _fu.ToArray();
                        EmitNal(nal, 0, nal.Length);
                        _fuActive = false;
                    }
                }
            }
            // 그 외 타입(STAP-B/MTAP 등)은 이 파이프라인에서 미사용

            if (marker) FlushAccessUnit(ptsUs);
        }

        void EmitNal(byte[] src, int off, int len)
        {
            if (len <= 0) return;
            int nalType = src[off] & 0x1F;

            if (nalType == 5) _auHasIdr = true; // IDR 슬라이스 -> 이 프레임은 키프레임

            // SPS/PPS는 파라미터셋으로 따로 보관(start code 포함본)
            if (nalType == 7 && (Sps == null || !SameNal(Sps, src, off, len)))
            {
                Sps = WithStartCode(src, off, len);
                TryParseSpsDimensions(src, off, len); // 해상도 갱신
            }
            else if (nalType == 8 && (Pps == null || !SameNal(Pps, src, off, len)))
                Pps = WithStartCode(src, off, len);

            _au.AddRange(StartCode);
            for (int i = 0; i < len; i++) _au.Add(src[off + i]);
        }

        void FlushAccessUnit(long ptsUs)
        {
            bool corrupt = _auCorrupt;
            bool hasIdr = _auHasIdr;
            _auCorrupt = false;
            _auHasIdr = false;

            void Discard()
            {
                _au.Clear();
                _fu.Clear();
                _fuActive = false;
            }

            if (waitForIdrOnLoss)
            {
                // 프레임 도중 손실 -> 버리고, 다음 IDR까지 대기 상태로 전환
                if (corrupt)
                {
                    _needKeyframe = true;
                    _statLoss++;
                    Discard();
                    return;
                }

                // 복구 대기 중: 깨끗한 IDR이 올 때까지 P-프레임을 버린다.
                // (참조가 끊긴 P-프레임을 디코더에 넣으면 깨짐이 다음 IDR까지 번짐)
                if (_needKeyframe && !hasIdr)
                {
                    _statWait++;
                    Discard();
                    return;
                }
                _needKeyframe = false;
            }
            else if (corrupt)
            {
                _statLoss++; // 통계만 집계, 프레임은 그대로 공급(디코더 오류은닉 -> 부드러움 우선)
            }

            if (_au.Count == 0) return;
            var au = new AccessUnit { data = _au.ToArray(), ptsUs = ptsUs };
            _au.Clear();

            Frames.Enqueue(au);
            _statDelivered++;
            // 지연 누적 방지: 오래된 프레임 드롭
            while (Frames.Count > maxQueued && Frames.TryDequeue(out _)) { }
        }

        // 초당 통계 로그 (adb logcat 로 확인). delivered≈30 이면 정상, loss/wait 크면 패킷 손실.
        void Update()
        {
            _statTimer += Time.deltaTime;
            if (_statTimer < 1f) return;
            _statTimer = 0f;
            Debug.Log($"[Rtp] 1s delivered={_statDelivered}fps loss={_statLoss} waitIDR={_statWait} queue={Frames.Count}");
            _statDelivered = _statLoss = _statWait = 0;
        }

        static byte[] WithStartCode(byte[] src, int off, int len)
        {
            var b = new byte[4 + len];
            Buffer.BlockCopy(StartCode, 0, b, 0, 4);
            Buffer.BlockCopy(src, off, b, 4, len);
            return b;
        }

        static bool SameNal(byte[] existingWithStart, byte[] src, int off, int len)
        {
            if (existingWithStart.Length != len + 4) return false;
            for (int i = 0; i < len; i++)
                if (existingWithStart[4 + i] != src[off + i]) return false;
            return true;
        }

        // ---- H.264 SPS 파싱 -> VideoWidth/Height (RBSP emulation prevention 제거 후 Exp-Golomb) ----
        void TryParseSpsDimensions(byte[] src, int off, int len)
        {
            try
            {
                var rbsp = new System.Collections.Generic.List<byte>(len);
                int zeros = 0;
                for (int i = off + 1; i < off + len; i++) // off = NAL 헤더(type 7), 그 다음이 RBSP
                {
                    byte b = src[i];
                    if (zeros >= 2 && b == 0x03) { zeros = 0; continue; } // emulation_prevention_three_byte
                    rbsp.Add(b);
                    zeros = (b == 0) ? zeros + 1 : 0;
                }

                var r = new BitReader(rbsp);
                int profile = r.U(8);
                r.U(8);   // constraint_set flags + reserved
                r.U(8);   // level_idc
                r.UE();   // seq_parameter_set_id
                if (profile == 100 || profile == 110 || profile == 122 || profile == 244 ||
                    profile == 44 || profile == 83 || profile == 86 || profile == 118 ||
                    profile == 128 || profile == 138 || profile == 139 || profile == 134 || profile == 135)
                {
                    int chroma = r.UE();
                    if (chroma == 3) r.U(1);
                    r.UE(); r.UE();       // bit_depth_luma/chroma_minus8
                    r.U(1);               // qpprime_y_zero_transform_bypass_flag
                    if (r.U(1) == 1)      // seq_scaling_matrix_present_flag
                    {
                        int cnt = (chroma != 3) ? 8 : 12;
                        for (int i = 0; i < cnt; i++)
                            if (r.U(1) == 1) SkipScalingList(r, i < 6 ? 16 : 64);
                    }
                }
                r.UE();                    // log2_max_frame_num_minus4
                int pocType = r.UE();
                if (pocType == 0) r.UE();  // log2_max_pic_order_cnt_lsb_minus4
                else if (pocType == 1)
                {
                    r.U(1); r.SE(); r.SE();
                    int n = r.UE();
                    for (int i = 0; i < n; i++) r.SE();
                }
                r.UE();                    // max_num_ref_frames
                r.U(1);                    // gaps_in_frame_num_value_allowed_flag
                int wMbs = r.UE() + 1;     // pic_width_in_mbs_minus1
                int hMap = r.UE() + 1;     // pic_height_in_map_units_minus1
                int frameMbsOnly = r.U(1);
                if (frameMbsOnly == 0) r.U(1); // mb_adaptive_frame_field_flag
                r.U(1);                    // direct_8x8_inference_flag
                int cl = 0, cr = 0, ct = 0, cb = 0;
                if (r.U(1) == 1) { cl = r.UE(); cr = r.UE(); ct = r.UE(); cb = r.UE(); } // frame_cropping

                int w = wMbs * 16;
                int h = hMap * 16 * (2 - frameMbsOnly);
                int unitX = 2;                        // 4:2:0 가정 (I420)
                int unitY = 2 * (2 - frameMbsOnly);
                w -= (cl + cr) * unitX;
                h -= (ct + cb) * unitY;
                if (w > 0 && h > 0) { VideoWidth = w; VideoHeight = h; }
            }
            catch { /* 파싱 실패 시 기존 값 유지 (오버레이가 기본값으로 폴백) */ }
        }

        static void SkipScalingList(BitReader r, int size)
        {
            int last = 8, next = 8;
            for (int i = 0; i < size; i++)
            {
                if (next != 0) { int delta = r.SE(); next = (last + delta + 256) % 256; }
                last = (next == 0) ? last : next;
            }
        }

        sealed class BitReader
        {
            readonly System.Collections.Generic.List<byte> _d;
            int _bit;
            public BitReader(System.Collections.Generic.List<byte> d) { _d = d; }

            public int U(int n)
            {
                int v = 0;
                for (int i = 0; i < n; i++)
                {
                    int idx = _bit >> 3;
                    int b = idx < _d.Count ? (_d[idx] >> (7 - (_bit & 7))) & 1 : 0;
                    v = (v << 1) | b;
                    _bit++;
                }
                return v;
            }

            public int UE() // Exp-Golomb unsigned
            {
                int zeros = 0;
                while (U(1) == 0 && zeros < 32) zeros++;
                int v = 0;
                for (int i = 0; i < zeros; i++) v = (v << 1) | U(1);
                return v + (1 << zeros) - 1;
            }

            public int SE() // Exp-Golomb signed
            {
                int k = UE();
                int m = (k + 1) >> 1;
                return (k & 1) == 1 ? m : -m;
            }
        }
    }
}
