using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections;

namespace ConsoleApplication1
{
    static class Program
    {
        public static bool IsRuning = false;
        static Thread VideoThread;
        static SSObj VideoObj;
        static Thread AudioThread;
        static SSObj AudioObj;
        class SSObj
        {
            public string type = "";
            public IPEndPoint EP1 = null;
            public IPEndPoint EP2 = null;
            public List<IPEndPoint> DistEPList = new List<IPEndPoint>();
            public Socket Socket;
            public long EP1Count = 0;
            public long EP2Count = 0;
            public SSObj(Socket socket)
            {
                this.Socket = socket;
            }

            public void Close()
            {
                if (Socket != null)
                {
                    try
                    {
                        Socket.Close();
                        EP1 = null;
                        EP2 = null;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("SSObj.Close() Error:" + ex.Message);
                    }
                }
            }
        }

        static void Main(string[] args)
        {
            CreateTrans(args);
            while (true)
            {
                string str = Console.ReadLine();
                if (str == "q")
                {
                    CloseTrans();
                    break;
                }
                else if (str == "p")
                {
                    CloseTrans();
                    CreateTrans(args);
                }
                else
                {
                    Console.WriteLine(String.Format("EP1 video:{0} audio:{1}", VideoObj.EP1Count, AudioObj.EP1Count));
                    Console.WriteLine(String.Format("EP2 video:{0} audio:{1}", VideoObj.EP2Count, AudioObj.EP2Count));
                }
            }
        }

        static void CreateTrans(string[] args)
        {
            IPAddress ip = IPAddress.Any;
            int videoPort = 3000;
            int audioPort = videoPort + 2;
            if (args.Length > 1) { ip = IPAddress.Parse(args[1]); }
            if (args.Length > 2) { videoPort = int.Parse(args[2]); }
            if (args.Length > 2) { videoPort = int.Parse(args[2]); }
            if (args.Length > 3) { audioPort = int.Parse(args[3]); }
            Socket videoSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            videoSocket.Bind(new IPEndPoint(ip, videoPort));
            Socket audioSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            audioSocket.Bind(new IPEndPoint(ip, audioPort));
            VideoObj = new SSObj(videoSocket);
            VideoObj.type = "Video";
            AudioObj = new SSObj(audioSocket);
            AudioObj.type = "Audio";

            IPAddress ip1 = null;
            int port1 = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith("ep="))
                {
                    string[] eps = args[i].Substring(3).Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var ep in eps)
                    {
                        IPAddress.TryParse(ep.Substring(0, ep.IndexOf(":")), out ip1);
                        int.TryParse(ep.Substring(ep.IndexOf(":") + 1), out port1);
                        VideoObj.DistEPList.Add(new IPEndPoint(ip1, port1));
                        AudioObj.DistEPList.Add(new IPEndPoint(ip1, port1 + 2));
                    }
                }
            }

            IsRuning = true;
            VideoThread = new Thread(new ParameterizedThreadStart(StartTran));
            VideoThread.Start(VideoObj);
            AudioThread = new Thread(new ParameterizedThreadStart(StartTran));
            AudioThread.Start(AudioObj);

            Console.WriteLine("CreateTrans: ");
            Console.WriteLine("Video: " + ip.ToString() + ":" + videoPort.ToString());
            Console.WriteLine("Audio: " + ip.ToString() + ":" + audioPort.ToString());
            Console.WriteLine("");
            for (int i = 0; i < VideoObj.DistEPList.Count; i++)
            {
                Console.WriteLine(String.Format("{0} 第{1}个分发客户端  {2}:{3}", "video", i + 1, VideoObj.DistEPList[i].Address, VideoObj.DistEPList[i].Port));
                Console.WriteLine(String.Format("{0} 第{1}个分发客户端  {2}:{3}", "audio", i + 1, AudioObj.DistEPList[i].Address, AudioObj.DistEPList[i].Port));
            }
        }

        static void CloseTrans()
        {
            Console.WriteLine("CloseTrans: begin");
            try
            {
                IsRuning = false;
                Thread.Sleep(500);
                VideoObj.Close();
                AudioObj.Close();
                Thread.Sleep(500);
                if (VideoThread.IsAlive)
                {
                    try
                    {
                        VideoThread.Abort();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("VideoThread.Abort() Error:" + ex.Message);
                    }
                }
                if (AudioThread.IsAlive)
                {
                    try
                    {
                        AudioThread.Abort();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("AudioThread.Abort() Error:" + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("CloseTrans Error:" + ex.Message);
            }
            Console.WriteLine("CloseTrans: end");
            Console.WriteLine("");
        }

        static void StartTran(Object obj)
        {
            SSObj ssObj = (SSObj)obj;
            IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            EndPoint remote = (EndPoint)sender;
            Queue<byte[]> epTmpQueue = new Queue<byte[]>(100 * 1024); // 避免第一个socket发了一堆数据，第二个socket还没上来早造成丢包
            byte[] buffer = new byte[512 * 1024];
            while (IsRuning)
            {
                try
                {
                    if (ssObj.Socket.Available == 0)
                    {
                        Thread.Sleep(20);
                        continue;
                    }
                    int size = ssObj.Socket.ReceiveFrom(buffer, ref remote);
                    IPEndPoint remoteEP = (IPEndPoint)remote;
                    if (ssObj.EP1 == null)  // 第一个socket第一次发数据
                    {
                        ssObj.EP1 = remoteEP;
                        Console.WriteLine(String.Format("{0} 第1个转发客户端  {1}:{2}", ssObj.type, remoteEP.Address, remoteEP.Port));
                    }
                    if (ssObj.EP2 == null && SameEP(ssObj.EP1, remoteEP)) // 第二个socket还没发过数据
                    {
                        byte[] tmpBuffer = new byte[size];
                        Array.Copy(buffer, tmpBuffer, size);
                        epTmpQueue.Enqueue(tmpBuffer); // 中转缓存
                        DistData(ssObj.Socket, ssObj.EP1, remoteEP, ssObj.DistEPList, buffer, size); // 分发
                        continue;
                    }

                    if (ssObj.EP1 != null && ssObj.EP2 == null) // 第二个socket第一次发
                    {
                        ssObj.EP2 = remoteEP;
                        Console.WriteLine(String.Format("{0} 第2个转发客户端  {1}:{2}", ssObj.type, remoteEP.Address, remoteEP.Port));
                        while (true)  // 发送中转缓存
                        {
                            if (epTmpQueue.Count == 0)
                            {
                                break;
                            }
                            else
                            {
                                byte[] tmpBuffer = epTmpQueue.Dequeue();
                                ssObj.EP1Count += tmpBuffer.Length;
                                ssObj.Socket.SendTo(tmpBuffer, 0, tmpBuffer.Length, SocketFlags.None, ssObj.EP2);
                                Thread.Sleep(10);
                            }
                        }
                    }

                    EndPoint targetEP = null;
                    if (SameEP(ssObj.EP1, remoteEP))
                    {
                        targetEP = ssObj.EP2;
                        ssObj.EP1Count += size;
                    }
                    else if (SameEP(ssObj.EP2, remoteEP))
                    {
                        targetEP = ssObj.EP1;
                        ssObj.EP2Count += size;
                    }
                    if (targetEP != null)
                    {
                        ssObj.Socket.SendTo(buffer, 0, size, SocketFlags.None, targetEP);
                    }
                    if (SameEP(ssObj.EP1, remoteEP))
                    {
                        for (int i = 0; i < VideoObj.DistEPList.Count; i++)
                        {
                            ssObj.Socket.SendTo(buffer, 0, size, SocketFlags.None, ssObj.DistEPList[i]);
                        }
                    }
                    DistData(ssObj.Socket, ssObj.EP1, remoteEP, ssObj.DistEPList, buffer, size); // 分发
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ssObj.type + " StartTran Error:" + ex.Message);
                    break;
                }
            }
        }

        static void DistData(Socket socket, IPEndPoint ep1, IPEndPoint ep2, List<IPEndPoint> ips, byte[] buffer, int size)
        {
            if (SameEP(ep1, ep2))
            {
                for (int i = 0; i < ips.Count; i++)
                {
                    socket.SendTo(buffer, 0, size, SocketFlags.None, ips[i]);
                }
            }
        }

        static bool SameEP(IPEndPoint ipe, IPEndPoint other)
        {
            return ipe.Address.Equals(other.Address) && ipe.Port == other.Port;
        }
    }
}
