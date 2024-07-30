using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Xenko.Core.Mathematics
{
    /// <summary>
    /// Simple helper class to handle saving and loading simple data to a file. Automatically compresses data and handles basic C# signed datatypes (long, int, string, bool etc.).
    /// Also supports basic Focus Engine types, like Vector3, Quaternions and Color4s (and arrays of them)
    /// </summary>
    public class SimpleSaver
    {
        public Dictionary<string, object> Data = new Dictionary<string, object>();

        /// <summary>
        /// Gets a simple path to use from ApplicationData, includes '/' at the end
        /// </summary>
        /// <param name="tag">subfolder to use (game name etc.)</param>
        public static string GetGoodPath(string tag)
        {
            string path = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/" + tag + "/";
            if (Directory.Exists(path) == false) Directory.CreateDirectory(path);
            return path;
        }

        public string Filename;

        private static BinaryWriter CompressedStream(Stream output)
        {
            GZipStream cmp = new GZipStream(output, CompressionMode.Compress);
            BufferedStream buffStrm = new BufferedStream(cmp, 65536);
            return new BinaryWriter(buffStrm);
        }

        private static BinaryReader UncompressedStream(Stream input)
        {
            GZipStream cmp = new GZipStream(input, CompressionMode.Decompress);
            return new BinaryReader(cmp);
        }
        
        /// <summary>
        /// Returns what the boolean was toggled to
        /// </summary>
        public bool ToggleBoolean(string name, bool defaultIfNew = true)
        {
            if (Data.TryGetValue(name, out object o) && o is bool b)
            {
                Data[name] = !b;
                return !b;
            }

            Data[name] = defaultIfNew;
            return defaultIfNew;
        }

        public T Get<T>(string name, T defaultValue = default)
        {
            if (Data.TryGetValue(name, out object o) && o is T t)
                return t;

            return defaultValue;
        }

        public void Set(string name, object value)
        {
            Data[name] = value;
        }

        public SimpleSaver(string filename = null, bool tryLoading = true)
        {
            if (filename != null)
            {
                Filename = filename;
                if (tryLoading) LoadFromFile();
            }
        }

        public void SaveToFile()
        {
            if (Filename == null) throw new InvalidOperationException("Can't save to file if Filename is not set!");
            using (BinaryWriter bw = CompressedStream(File.Open(Filename, FileMode.Create)))
            {
                Write(bw);
            }
        }

        public bool LoadFromFile()
        {
            if (Filename == null) throw new InvalidOperationException("Can't load from a file if Filename is not set!");
            if (!File.Exists(Filename)) return false;
            Data.Clear();
            using (BinaryReader br = UncompressedStream(File.Open(Filename, FileMode.Open)))
            {
                Read(br);
            }
            return true;
        }

        public void Read(BinaryReader reader)
        {
            int cnt = reader.ReadInt32();
            int len, i;
            for (int j=0; j<cnt; j++)
            {
                string k = reader.ReadString();
                string key = k.Substring(1);
                switch (k[0])
                {
                    case 'S':
                        Data[key] = reader.ReadString();
                        break;
                    case 'F':
                        Data[key] = reader.ReadSingle();
                        break;
                    case 'I':
                        Data[key] = reader.ReadInt32();
                        break;
                    case 's':
                        Data[key] = reader.ReadInt16();
                        break;
                    case 'b':
                        Data[key] = reader.ReadByte();
                        break;
                    case 'B':
                        Data[key] = reader.ReadBoolean();
                        break;
                    case 'D':
                        Data[key] = reader.ReadDouble();
                        break;
                    case 'L':
                        Data[key] = reader.ReadInt64();
                        break;
                    case '1':
                        int[] arr = new int[reader.ReadInt32()];
                        for (i=0; i<arr.Length; i++)
                            arr[i] = reader.ReadInt32();
                        Data[key] = arr;
                        break;
                    case '2':
                        float[] farr = new float[reader.ReadInt32()];
                        for (i = 0; i < farr.Length; i++)
                            farr[i] = reader.ReadSingle();
                        Data[key] = farr;
                        break;
                    case '3':
                        string[] sarr = new string[reader.ReadInt32()];
                        for (i = 0; i < sarr.Length; i++)
                            sarr[i] = reader.ReadString();
                        Data[key] = sarr;
                        break;
                    case '4':
                        bool[] barr = new bool[reader.ReadInt32()];
                        for (i = 0; i < barr.Length; i++)
                            barr[i] = reader.ReadBoolean();
                        Data[key] = barr;
                        break;
                    case '5':
                        Data[key] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        break;
                    case 'Q':
                        Data[key] = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        break;
                    case 'C':
                        Data[key] = new Color4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        break;
                    case '6':
                        Vector3[] varr = new Vector3[reader.ReadInt32()];
                        for (i = 0; i < varr.Length; i++)
                            varr[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        Data[key] = varr;
                        break;
                    case '7':
                        Quaternion[] qarr = new Quaternion[reader.ReadInt32()];
                        for (i = 0; i < qarr.Length; i++)
                            qarr[i] = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        Data[key] = qarr;
                        break;
                    case '8':
                        Color4[] carr = new Color4[reader.ReadInt32()];
                        for (i = 0; i < carr.Length; i++)
                            carr[i] = new Color4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        Data[key] = carr;
                        break;
                    default:
                        throw new InvalidDataException("I don't understand '" + k + "' type to read!");
                }
            }
        }

        public void Write(BinaryWriter output)
        {
            output.Write(Data.Count);
            foreach (KeyValuePair<string, object> kvp in Data)
            {
                if (kvp.Value is string s)
                {
                    output.Write("S" + kvp.Key);
                    output.Write(s);
                } 
                else if (kvp.Value is int[] ia)
                {
                    output.Write("1" + kvp.Key);
                    output.Write(ia.Length);
                    for (int i = 0; i < ia.Length; i++)
                        output.Write(ia[i]);
                }
                else if (kvp.Value is float[] fa)
                {
                    output.Write("2" + kvp.Key);
                    output.Write(fa.Length);
                    for (int i = 0; i < fa.Length; i++)
                        output.Write(fa[i]);
                }
                else if (kvp.Value is string[] sa)
                {
                    output.Write("3" + kvp.Key);
                    output.Write(sa.Length);
                    for (int i = 0; i < sa.Length; i++)
                        output.Write(sa[i]);
                }
                else if (kvp.Value is bool[] ba)
                {
                    output.Write("4" + kvp.Key);
                    output.Write(ba.Length);
                    for (int i = 0; i < ba.Length; i++)
                        output.Write(ba[i]);
                }
                else if (kvp.Value is Vector3 v3)
                {
                    output.Write("5" + kvp.Key);
                    output.Write(v3.X);
                    output.Write(v3.Y);
                    output.Write(v3.Z);
                }
                else if (kvp.Value is Vector3[] v3a)
                {
                    output.Write("6" + kvp.Key);
                    output.Write(v3a.Length);
                    for (int i = 0; i < v3a.Length; i++)
                    {
                        output.Write(v3a[i].X);
                        output.Write(v3a[i].Y);
                        output.Write(v3a[i].Z);
                    }
                }
                else if (kvp.Value is Quaternion q)
                {
                    output.Write("Q" + kvp.Key);
                    output.Write(q.X);
                    output.Write(q.Y);
                    output.Write(q.Z);
                    output.Write(q.W);
                }
                else if (kvp.Value is Quaternion[] qa)
                {
                    output.Write("7" + kvp.Key);
                    output.Write(qa.Length);
                    for (int i = 0; i < qa.Length; i++)
                    {
                        output.Write(qa[i].X);
                        output.Write(qa[i].Y);
                        output.Write(qa[i].Z);
                        output.Write(qa[i].W);
                    }
                }
                else if (kvp.Value is Color4 c)
                {
                    output.Write("C" + kvp.Key);
                    output.Write(c.R);
                    output.Write(c.G);
                    output.Write(c.B);
                    output.Write(c.A);
                }
                else if (kvp.Value is Color4[] ca)
                {
                    output.Write("8" + kvp.Key);
                    output.Write(ca.Length);
                    for (int i = 0; i < ca.Length; i++)
                    {
                        output.Write(ca[i].R);
                        output.Write(ca[i].G);
                        output.Write(ca[i].B);
                        output.Write(ca[i].A);
                    }
                }
                else if (kvp.Value is float f)
                {
                    output.Write("F" + kvp.Key);
                    output.Write(f);
                }
                else if (kvp.Value is int i)
                {
                    output.Write("I" + kvp.Key);
                    output.Write(i);
                }
                else if (kvp.Value is short ss)
                {
                    output.Write("s" + kvp.Key);
                    output.Write(ss);
                }
                else if (kvp.Value is byte b)
                {
                    output.Write("b" + kvp.Key);
                    output.Write(b);
                }
                else if (kvp.Value is bool bb)
                {
                    output.Write("B" + kvp.Key);
                    output.Write(bb);
                }
                else if (kvp.Value is double d)
                {
                    output.Write("D" + kvp.Key);
                    output.Write(d);
                }
                else if (kvp.Value is long l)
                {
                    output.Write("L" + kvp.Key);
                    output.Write(l);
                }
                else throw new InvalidDataException("Can't process type: " + (kvp.Value?.GetType()?.Name ?? "null"));
            }
        }
    }
}
