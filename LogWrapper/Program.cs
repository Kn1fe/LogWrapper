using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace LogWrapper
{
    class Program
    {
        public static ManualResetEvent allDone = new ManualResetEvent(false);

        static Dictionary<int, int> qg = new Dictionary<int, int>();

        static void Main(string[] args)
        {
            StreamReader sr = new StreamReader(AppDomain.CurrentDomain.BaseDirectory + "//gold.txt");
            while (!sr.EndOfStream)
            {
                string[] s = sr.ReadLine().Split('=');
                qg.Add(int.Parse(s[0]), int.Parse(s[1]));
            }
            sr.Close();
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Loopback, 11102);
            Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(256);

                while (true)
                {
                    allDone.Reset();
                    Console.WriteLine("Waiting for a connection...");
                    listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);
                    allDone.WaitOne();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            Console.WriteLine("\nPress ENTER to continue...");
            Console.Read();
        }

        public static void AcceptCallback(IAsyncResult ar)
        {
            allDone.Set();
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);
            StateObject state = new StateObject()
            {
                workSocket = handler
            };
            handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
        }

        public static void ReadCallback(IAsyncResult ar)
        {
            StateObject state = (StateObject)ar.AsyncState;
            Socket handler = state.workSocket;
            int bytesRead = handler.EndReceive(ar);
            if (bytesRead > 0)
            {
                handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0, new AsyncCallback(ReadCallback), state);
                PacketsReader pr = new PacketsReader(state.buffer, -1);
                uint OpCode = pr.Cuint();
                if (OpCode == 61)
                {
                    pr.Cuint();
                    string s = pr.ReadASCIIString();
                    string[] parse = s.Split(':');
                    if (parse.Length >= 5)
                    {
                        if (s.Contains("DeliverByAwardData"))
                        {
                            int taskid = Convert.ToInt32(parse[3].Split('=')[1]);
                            if (qg.ContainsKey(taskid))
                            {
                                int roleid = int.Parse(parse[2].Split('=')[1]);
                                PacketsWriter pw = new PacketsWriter(3412);
                                pw.PackInt(-1);
                                pw.PackInt(roleid);
                                pr = new PacketsReader(pw.SendData(new IPEndPoint(IPAddress.Loopback, 29400), true, false), 0);
                                int userid = pr.ReadInt32();
                                pw = new PacketsWriter(521);
                                pw.PackInt(userid);
                                pw.PackInt(qg[taskid]);
                                pw.SendData(new IPEndPoint(IPAddress.Loopback, 29400), false, false);
                                Console.WriteLine("Add cash: {0}, userid: {1}, roleid: {2}", qg[taskid], userid, roleid);
                            }
                        }
                    }
                }
            }
        }
    }

    public class StateObject
    {
        public Socket workSocket = null;
        public const int BufferSize = 16192;
        public byte[] buffer = new byte[BufferSize];
        public string log_name = "";
    }

    public class PacketsWriter
    {
        List<byte> Data = new List<byte>();
        uint OpCode = 0;

        public PacketsWriter(uint OpCode)
        {
            this.OpCode = OpCode;
        }

        public void PackInt(uint a)
        {
            byte[] d = BitConverter.GetBytes(a);
            Array.Reverse(d);
            Data.AddRange(d);
        }

        public void PackInt(int a)
        {
            byte[] d = BitConverter.GetBytes(a);
            Array.Reverse(d);
            Data.AddRange(d);
        }

        public void PackCuint(uint a)
        {
            Data.AddRange(cuint(a));
        }
        public void addCuint(uint a, int pos)
        {
            Data.InsertRange(pos, cuint(a));
        }

        public void PackBytes(byte[] b)
        {
            Data.AddRange(b);
        }

        public void PackByte(byte b)
        {
            Data.Add(b);
        }

        public void PackString(string s)
        {
            byte[] r = Encoding.Convert(Encoding.UTF8, Encoding.Unicode, Encoding.UTF8.GetBytes(s));
            Data.AddRange(cuint((uint)r.Length));
            Data.AddRange(r);
        }

        public byte[] cuint(uint v)
        {
            if (v < 64)
            {
                return new byte[] { (byte)v };
            }
            if (v < 16384)
            {
                byte[] b = BitConverter.GetBytes((ushort)(v + 0x8000));
                Array.Reverse(b);
                return b;
            }
            if (v < 536870912)
            {
                byte[] b = BitConverter.GetBytes(v + 536870912);
                Array.Reverse(b);
                return b;
            }

            return new byte[] { };
        }

        public void debugData()
        {
            foreach (byte b in Data)
            {
                Console.Write(b);
            }
            Console.WriteLine("");
            Console.WriteLine("");
        }

        public byte[] SendData(IPEndPoint ip, bool Recive = false, bool Empty = false)
        {
            List<byte> buffer = new List<byte>();
            buffer.AddRange(cuint(OpCode));
            buffer.AddRange(cuint((uint)Data.Count));
            buffer.AddRange(Data.ToArray());
            Socket s = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                byte[] data = new byte[32768];
                s.Connect(ip);
                if (Empty)
                {
                    s.Receive(data);
                }
                s.Send(buffer.ToArray());
                if (Recive)
                {
                    int bytesRead;
                    bytesRead = s.Receive(data);
                    return data;
                }
                s.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            return new byte[0];
        }
    }

    public class PacketsReader
    {
        BinaryReader br;

        public PacketsReader(byte[] data, int RemoveHeaderType)
        {
            br = new BinaryReader(new MemoryStream(data));
            switch(RemoveHeaderType)
            {
                case 0:
                    br.BaseStream.Seek(2, SeekOrigin.Begin);
                    if (br.ReadByte() == 8)
                    {
                        br.BaseStream.Seek(12, SeekOrigin.Begin);
                    }
                    else
                    {
                        br.BaseStream.Seek(11, SeekOrigin.Begin);
                    }
                    break;
            }
        }

        public uint Cuint()
        {
            uint res = 0;
            long pos = br.BaseStream.Position;
            byte b = br.ReadByte();
            if ((res = b & (uint)0xFF) > 128L)
            {
                br.BaseStream.Seek(pos, SeekOrigin.Begin);
                short sh = br.ReadInt16();
                return (uint)sh & 0xFFFF ^ 0x8000;
            }
            return res;
        }

        public string ReadASCIIString()
        {
            uint len = Cuint();
            return Encoding.ASCII.GetString(br.ReadBytes((int)len));
        }

        public string ReadOctet()
        {
            uint len = Cuint();
            return Encoding.Unicode.GetString(br.ReadBytes((int)len));
        }

        public int ReadInt32()
        {
            byte[] i = br.ReadBytes(4);
            Array.Reverse(i);
            return BitConverter.ToInt32(i, 0);
        }

        public int ReadInt32_v2()
        {
            return br.ReadInt32();
        }

        public byte ReadByte()
        {
            return br.ReadByte();
        }
    }
}
