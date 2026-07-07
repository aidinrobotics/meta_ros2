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
    /// LAN·저지연 전제라 seq 재정렬은 하지 않는다(유실 시 다음 키프레임에서 복구).
    /// </summary>
    public class RtpH264Receiver : MonoBehaviour
    {
        [Tooltip("Jetson udpsink 이 쏘는 포트")]
        public int port = 5600;

        [Tooltip("큐가 이보다 커지면 오래된 프레임을 버려 지연 누적 방지")]
        public int maxQueued = 3;

        public struct AccessUnit
        {
            public byte[] data;   // start code 포함 Annex-B
            public long ptsUs;    // presentation timestamp (us)
        }

        public readonly ConcurrentQueue<AccessUnit> Frames = new ConcurrentQueue<AccessUnit>();

        public byte[] Sps { get; private set; }   // start code 포함
        public byte[] Pps { get; private set; }   // start code 포함
        public bool HasParameterSets => Sps != null && Pps != null;

        static readonly byte[] StartCode = { 0x00, 0x00, 0x00, 0x01 };

        UdpClient _udp;
        Thread _thread;
        volatile bool _running;

        // 현재 조립 중인 access unit / FU-A 조각 버퍼
        readonly System.Collections.Generic.List<byte> _au = new System.Collections.Generic.List<byte>(256 * 1024);
        readonly System.Collections.Generic.List<byte> _fu = new System.Collections.Generic.List<byte>(256 * 1024);
        bool _fuActive;

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

            // SPS/PPS는 파라미터셋으로 따로 보관(start code 포함본)
            if (nalType == 7 && (Sps == null || !SameNal(Sps, src, off, len)))
                Sps = WithStartCode(src, off, len);
            else if (nalType == 8 && (Pps == null || !SameNal(Pps, src, off, len)))
                Pps = WithStartCode(src, off, len);

            _au.AddRange(StartCode);
            for (int i = 0; i < len; i++) _au.Add(src[off + i]);
        }

        void FlushAccessUnit(long ptsUs)
        {
            if (_au.Count == 0) return;
            var au = new AccessUnit { data = _au.ToArray(), ptsUs = ptsUs };
            _au.Clear();

            Frames.Enqueue(au);
            // 지연 누적 방지: 오래된 프레임 드롭
            while (Frames.Count > maxQueued && Frames.TryDequeue(out _)) { }
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
    }
}
