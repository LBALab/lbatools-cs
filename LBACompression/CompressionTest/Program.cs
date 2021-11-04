using System;
using System.IO;
using System.Collections.Generic;
using LBACompression;

namespace CompressionTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("LBA Compression Test Program");
            Console.WriteLine("");
            
            if (args.GetUpperBound(0) != 3)
            {
                Console.WriteLine("Usage: COMPRESS D N FILENAME.EXT FILENAME.EXT");
                Console.WriteLine("");
                Console.WriteLine("D: C = Compress, D = Decompress");
                Console.WriteLine("N: 1 = LZSS, 2 = LZMIT");
            }
            else
            {
                int Direction, Type;
                int InLen, OutLen;
                BinaryReader bR;
                BinaryWriter bW;
                FileStream fSR, fSW;
                byte[] In, Out;

                if ((args[0] == "C") || (args[0] == "c"))
                    Direction = 0;  /* Compress. */
                else if ((args[0] == "D") || (args[0] == "d"))
                    Direction = 1;  /* Decompress. */
                else
                {
                    Console.WriteLine("Invalid direction: " + args[0]);
                    return;
                }

                Type = Convert.ToInt32(args[1]);

                if ((Type < 1) || (Type > 2))
                {
                    Console.WriteLine("Invalid " + ((Direction == 1) ? "de" : "") + "compression type: " + args[1]);
                    return;
                }
                else if (Type == 1)
                {
                    Console.WriteLine(((Direction == 1) ? "Decompressing" : "Compressing") + " using LZSS");
                }
                else if (Type == 2)
                {
                    Console.WriteLine(((Direction == 1) ? "Decompressing" : "Compressing") + " using LZMIT");
                }

                if (!File.Exists(args[2]))
                {
                    Console.WriteLine("File does not exist: " + args[2]);
                    return;
                }

                if (File.Exists(args[3]))
                {
                    Console.WriteLine("File already exists: " + args[3]);
                    File.Delete(args[3]);
                }

                using (fSR = File.OpenRead(args[2]))
                {
                    bR = new BinaryReader(fSR);
                    InLen = (int)fSR.Length;
                    In = bR.ReadBytes(InLen);
                }

                if (Type == 1)
                {
                    /* Compression type 1 (LZSS) */
                    CompressLZSS cLZSS = new CompressLZSS(In, InLen);

                    if (Direction == 1)
                        cLZSS.Decompress();
                    else
                        cLZSS.Compress();

                    Out = cLZSS.GetDst();
                    OutLen = cLZSS.GetDstLen();
                }
                else
                {
                    /* Compression type 2 (LZMIT) */
                    CompressLZMIT cLZMIT = new CompressLZMIT(In, InLen);

                    if (Direction == 1)
                        cLZMIT.Decompress();
                    else
                        cLZMIT.Compress();

                    Out = cLZMIT.GetDst();
                    OutLen = cLZMIT.GetDstLen();
                }

                using (fSW = File.OpenWrite(args[3]))
                {
                    bW = new BinaryWriter(fSW);
                    bW.Write(Out, 0, OutLen);
                }

                Console.WriteLine(((Direction == 1) ? "Decompressed:" : "Compressed:"));
                Console.WriteLine("    Source file: " + args[2] + " (" + InLen + " bytes)");
                Console.WriteLine("    Destination file: " + args[3] + " (" + OutLen + " bytes)");
            }
        }
    }
}
