// The information for decrypting/extracting or even modifying the frames
// of a cinematic is based on the source code of:
//
// https://nihav.org/game_tool.html
//
// Filename: na_game_tool-0.4.0.tar.bz2
//
// I don't know the name of the author, so, all the info credits go to him/her.

using Microsoft.VisualBasic;
using System;
using System.IO.Enumeration;
using System.Text;

namespace LoftANM
{
    public static class ANMDecoder
    {
        const int WIDTH = 640;
        const int HEIGHT = 400;
        const int MAX_PALETTE_BYTES = 768;
        const int MAX_IMAGE_SIZE = WIDTH * HEIGHT;

        static BinaryReader readerAnm;

        public struct Chunk
        {
            public ushort chunk_id;
            public int chunk_size;
            public int chunk_offset;        // This is only for dump extraction

            public byte[] pal;
            public byte[] chunkdata;
            public byte[] chunkimg;

            public bool bIsImage;

            public Chunk()
            {
                pal = [];
                chunkdata = [];
                chunkimg = [];

                bIsImage = false;
            }
        }


        public struct Frame
        {
            public List<Chunk> chunks;
            public ushort nchunks;

            public Frame()
            {
                chunks = [];
            }
        }

        public struct Animation
        {
            public List<Frame> frames;

            public ushort nframes;
            public ushort version;

            public Animation()
            {
                frames = [];
                nframes = 0;
                version = 0;
            }

        }

        static Animation anim;
        static byte[] lastImage, lastPal;

        public static void ReadChunkPal(ref Chunk inChunk)
        {
            inChunk.pal = readerAnm.ReadBytes(inChunk.chunk_size);
        }


        public static byte[] Reverse(byte[] tmpFrame)
        {
            int w, h, hinv;
            hinv = HEIGHT - 1;

            byte[] tmpRevFrame = new byte[tmpFrame.Length];

            for (h = 0; h < HEIGHT; h++)
            {
                for (w = 0; w < WIDTH; w++)
                {
                    tmpRevFrame[hinv * WIDTH + w] = tmpFrame[h * WIDTH + w];
                }

                hinv--;
            }

            return tmpRevFrame;
        }


        public static void UnpackIntraRLE(ref Chunk tmpChunk)
        {
            int pos, i, len;
            byte clr, op;

            MemoryStream memChunkData = new(tmpChunk.chunkdata);
            BinaryReader readerChunkData = new(memChunkData);

            tmpChunk.chunkimg = new byte[MAX_IMAGE_SIZE];

            pos = 0;

            while (pos < tmpChunk.chunkimg.Length)
            {
                op = readerChunkData.ReadByte();

                if (op < 0x80)
                {
                    len = (op + 1) * 2;
                    
                    if (pos + len > tmpChunk.chunkimg.Length)
                        throw new Exception("UnpackIntraRLE: You have exceeded the length of the image.\n");

                    for (i = pos; i < pos + len; i += 2)
                    {
                        clr = readerChunkData.ReadByte();
                        tmpChunk.chunkimg[i] = clr;
                        tmpChunk.chunkimg[i + 1] = clr;
                    }

                    pos += len;
                }
                else
                {
                    len = (257 - op) * 2;
                    
                    if (pos + len > tmpChunk.chunkimg.Length)
                        throw new Exception("UnpackIntraRLE: You have exceeded the length of the image.\n");

                    clr = readerChunkData.ReadByte();

                    for (i = pos; i < pos + len; i++)
                    {
                        tmpChunk.chunkimg[i] = clr;
                    }

                    pos += len;
                }
            }

            memChunkData.Close();

            tmpChunk.chunkimg = Reverse(tmpChunk.chunkimg);
        }


        public static void UnpackInterRLE(ref Chunk tmpChunk)
        {
            ushort numUpd, skipX, skipY, count;
            int x, y, i, j, k, nlines, lineStartIndex;
            byte clr;

            tmpChunk.chunkimg = new byte[MAX_IMAGE_SIZE];

            if (tmpChunk.chunkimg.Length < MAX_IMAGE_SIZE)
            {
                throw new ArgumentOutOfRangeException(
                                nameof(tmpChunk.chunkimg), 
                                $"Destination buffer must be at least {MAX_IMAGE_SIZE} bytes long for the current WIDTH and HEIGHT settings."
                                                     );
            }

            MemoryStream memChunkData = new(tmpChunk.chunkdata);
            BinaryReader readerChunkData = new(memChunkData);

            numUpd = readerChunkData.ReadUInt16();
            x = 0; // Represents the column offset in the destination buffer

            for (i = 0; i < numUpd; i++)
            {
                skipX = readerChunkData.ReadUInt16();

                x += skipX * 2;

                if (x > WIDTH)
                    throw new Exception("UnpackInterRLE: Validation failed, x > WIDTH.");

                nlines = readerChunkData.ReadUInt16();
                y = 0; // Represents the row offset in the destination buffer

                for (j = 0; j < nlines; j++)
                {
                    skipY = readerChunkData.ReadUInt16();
                    y += skipY;

                    if (y > HEIGHT)
                        throw new Exception("UnpackInterRLE: Validation failed, y > HEIGHT.");

                    count = readerChunkData.ReadUInt16();   // Number of lines to update

                    if (y + count > HEIGHT)
                        throw new Exception("UnpackInterRLE: Validation failed, y + count > HEIGHT.");

                    // This loop handles the Rust `dst[x + y * WIDTH..].chunks_mut(WIDTH).take(count)` logic.
                    // It iterates 'count' times, each time processing a 'line' (a row chunk of WIDTH bytes).
                    for (k = 0; k < count; k++)
                    {
                        clr = readerChunkData.ReadByte(); // The color byte to apply

                        // Calculate the starting index for the current 'line' (row)
                        // The line starts at (x, y + k) in the 2D grid.
                        lineStartIndex = x + (y + k) * WIDTH;

                        // Apply the color to the first two elements of the 'line' (chunk)
                        // This assumes `line[0]` and `line[1]` from the Rust code
                        // are the first two bytes of the `WIDTH`-sized chunk.
                        if (lineStartIndex + 1 >= tmpChunk.chunkimg.Length)
                        {
                            throw new Exception($"Destination buffer index out of bounds during pixel write. Index: {lineStartIndex + 1}, Buffer Length: {tmpChunk.chunkimg.Length}.");
                        }

                        tmpChunk.chunkimg[lineStartIndex] = clr;
                        tmpChunk.chunkimg[lineStartIndex + 1] = clr;
                    }

                    y += count; // Advance y by the number of lines processed
                }
            }

            memChunkData.Close();

            tmpChunk.chunkimg = Reverse(tmpChunk.chunkimg);
        }


        public static void PutInterlaced(ReadOnlySpan<byte> src, int offset, Span<byte> dst)
        {
            // Calculate the starting index for the destination span.
            // Rust's `usize` is typically mapped to `int` in C# for array indexing.
            int dstStartIndex = WIDTH * 51 + offset * 2;

            // Ensure the destination span has enough room from the calculated start index
            // to prevent ArgumentOutOfRangeException.
            if (dstStartIndex >= dst.Length)
            {
                // The destination buffer is too short or the offset is too large.
                // Depending on desired error handling, you might throw an exception,
                // log a warning, or simply return. Returning is a common approach
                // for "chunks_exact" like behavior where incomplete data is ignored.
                return;
            }

            // Create a mutable span representing the working area of the destination buffer.
            Span<byte> dstWorkingSpan = dst.Slice(dstStartIndex);

            // Calculate how many full 'lines' can be processed from both source and destination.
            // Rust's `chunks_exact` means we only iterate over complete chunks.
            int dstLineCapacity = dstWorkingSpan.Length / WIDTH;
            int srcLineCapacity = src.Length / 80;

            // The outer loop iterates for the minimum number of complete lines available in both.
            int numOuterLines = Math.Min(dstLineCapacity, srcLineCapacity);

            for (int i = 0; i < numOuterLines; i++)
            {
                // Get the current destination line (a mutable span of WIDTH bytes).
                // This corresponds to `dline` in the Rust code.
                Span<byte> dline = dstWorkingSpan.Slice(i * WIDTH, WIDTH);

                // Get the current source line (a read-only span of 80 bytes).
                // This corresponds to `sline` in the Rust code.
                ReadOnlySpan<byte> sline = src.Slice(i * 80, 80);

                // Calculate how many full 'pairs' can be processed in the destination line
                // and how many pixels are in the source line.
                int dlinePairCapacity = dline.Length / 8; // Each 'pair' chunk is 8 bytes
                int slinePixelCapacity = sline.Length;    // Each 'pixel' is 1 byte

                // The inner loop iterates for the minimum number of complete pairs/pixels available.
                // This corresponds to the `zip` behavior of `dline.chunks_exact_mut(8).zip(sline.iter())`.
                int numInnerPairs = Math.Min(dlinePairCapacity, slinePixelCapacity);

                for (int j = 0; j < numInnerPairs; j++)
                {
                    // Get the current destination 'pair' (a mutable span of 8 bytes).
                    // This corresponds to `pair` in the Rust code.
                    Span<byte> pair = dline.Slice(j * 8, 8);

                    // Get the current source pixel value.
                    // This corresponds to `&pix` in the Rust code.
                    byte pix = sline[j];

                    // Assign the pixel value to the first two bytes of the 8-byte destination 'pair'.
                    // The remaining bytes (pair[2] to pair[7]) are left as they were,
                    // mimicking the exact Rust behavior.
                    pair[0] = pix;
                    pair[1] = pix;
                }
            }
        }


        public static void LoadFrame(int cur_frame)
        {
            int cur_chunk, i_iter;
            byte[] tmpData;
            Chunk tmpChunk;

            Frame tmpFrame = new()
            {
                nchunks = readerAnm.ReadUInt16()
            };

            for (cur_chunk = 0; cur_chunk < tmpFrame.nchunks; cur_chunk++)
            {

                tmpChunk = new()
                {
                    chunk_offset = (int)readerAnm.BaseStream.Position,
                    chunk_id = readerAnm.ReadUInt16(),
                    chunk_size = readerAnm.ReadInt32()
                };

                switch(tmpChunk.chunk_id)
                {
                    case 0:
                        // 0 READ PALETTE (768 bytes)
                        if (tmpChunk.chunk_size > MAX_PALETTE_BYTES)
                            Console.WriteLine("The Frame: " + cur_frame.ToString() + 
                                              ", Chunk: " + tmpChunk.chunk_id.ToString() +
                                              " has a size greater than the one of the palette.\n");
                        else
                        {
                            // Read palette
                            ReadChunkPal(ref tmpChunk);
                        }

                        break;

                    case 1:
                        // CLEAR PALETTE (PUT ZEROS)
                        if (tmpChunk.chunk_size != 0)
                            Console.WriteLine("The Frame: " + cur_frame.ToString() +
                                              ", Chunk: " + tmpChunk.chunk_id.ToString() +
                                              " has a size for a palette greater than zero.\n");
                        else
                        {
                            Array.Fill(tmpChunk.pal, (byte)0);
                        }

                        break;

                    case 2:
                        //2 UNPACK INTRA RLE
                        tmpChunk.chunkdata = readerAnm.ReadBytes(tmpChunk.chunk_size);

                        if (tmpChunk.chunk_size > 0) tmpChunk.bIsImage = true;

                        UnpackIntraRLE(ref tmpChunk);

                        break;

                    case 3:
                        // 3 UNPACK INTER RLE
                        tmpChunk.chunkdata = readerAnm.ReadBytes(tmpChunk.chunk_size);

                        if (tmpChunk.chunk_size > 0) tmpChunk.bIsImage = true;

                        UnpackInterRLE(ref tmpChunk);

                        break;

                    case 4:
                        // 4 REPEAT FRAME DATA AT OFFSET
                        tmpChunk.chunkdata = readerAnm.ReadBytes(tmpChunk.chunk_size);

                        //println!("mode 4 is untested");
                        //validate!(chunk_size >= 6);
                        //let _count = br.read_u16le() ?;
                        //let _offset = br.read_u32le() ?;
                        // apparently it repeats decoding delta frame
                        // at the given offset the provided amount of times
                        break;

                    case 5:
                        // 5 COMMAND WITH 2 * 16BIT PARAMETERS
                        tmpChunk.chunkdata = readerAnm.ReadBytes(tmpChunk.chunk_size);

                        break;

                    case 6:
                        // 6 COMMAND WITH 1 * 16BIT PARAMETER
                        tmpChunk.chunkdata = readerAnm.ReadBytes(tmpChunk.chunk_size);

                        break;

                    case 7:
                        // 7 COMMAND WITH 2 * 16BIT PARAMETERS
                        //   SHOW SUBTITLE OF T (FILE)
                        tmpChunk.chunkdata = readerAnm.ReadBytes(tmpChunk.chunk_size);

                        break;

                    case 8:
                        // 8 PUT INTERLACED RLE
                        if (!(tmpChunk.chunk_size == 0x14000))
                        {
                            Console.WriteLine("The Frame: " + cur_frame.ToString() +
                                              ", Chunk: " + tmpChunk.chunk_id.ToString() +
                                              "has not a size of 0x14000 bytes.\n");
                        }
                        else
                        {
                            if (tmpChunk.chunk_size > 0) tmpChunk.bIsImage = true;

                            tmpChunk.chunkdata = new byte[tmpChunk.chunk_size];
                            tmpChunk.chunkimg = new byte[MAX_IMAGE_SIZE];

                            for (i_iter = 0; i_iter < 4; i_iter++)
                            {
                                tmpData = readerAnm.ReadBytes(0x5000);
                                Array.Copy(tmpData, 0, tmpChunk.chunkdata, i_iter * 0x5000, 0x5000);

                                PutInterlaced(tmpData, i_iter, tmpChunk.chunkimg);
                            }

                            tmpChunk.chunkimg = Reverse(tmpChunk.chunkimg);
                        }

                        break;

                    default:
                        // 9 SKIP CHUNK
                        tmpChunk.chunkdata = readerAnm.ReadBytes(tmpChunk.chunk_size);

                        // _ => br.read_skip(chunk_size) ?;
                        break;

                }

                tmpFrame.chunks.Add(tmpChunk);
            }

            anim.frames.Add(tmpFrame);
        }


        public static void LoadANM(string filename)
        {
            byte[] anmInput;
            int cur_frame;

            try
            {
                // Read full file
                anmInput = File.ReadAllBytes(filename);

                // Process it
                MemoryStream memAnm = new(anmInput);
                readerAnm = new(memAnm);

                anim = new()
                {
                    nframes = readerAnm.ReadUInt16()
                };
                
                if (anim.nframes < 1 || anim.nframes > 1024)
                {
                    Console.WriteLine("The number of frames can not be below 1 or exceed 1024.\n");
                }
                else
                {
                    anim.version = readerAnm.ReadUInt16();

                    for (cur_frame = 0; cur_frame < anim.nframes; cur_frame++)
                    {
                        LoadFrame(cur_frame);
                    }

                }

                memAnm.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.Message);
            }
        }


        public static void WriteOneByte(BinaryWriter writerTGA, int inputByte)
        {
            writerTGA.Write((byte)inputByte);
        }


        //public static void SaveTGA(string filename, int i_nframe, int i_nchunk)
        public static void SaveTGAFull(string filename, int i_nframe)
        {
            int i;
            byte[] tmpPAL = new byte[MAX_PALETTE_BYTES];

            try
            {
                MemoryStream memTGA = new();
                BinaryWriter writerTGA = new(memTGA);

                WriteOneByte(writerTGA, 0);
                WriteOneByte(writerTGA, 1);
                WriteOneByte(writerTGA, 1);

                // Palette
                WriteOneByte(writerTGA, 0);
                WriteOneByte(writerTGA, 0);
                WriteOneByte(writerTGA, 0);
                WriteOneByte(writerTGA, 1);
                WriteOneByte(writerTGA, 24);

                WriteOneByte(writerTGA, 0);
                WriteOneByte(writerTGA, 0);
                WriteOneByte(writerTGA, 0);
                WriteOneByte(writerTGA, 0);


                writerTGA.Write((ushort)WIDTH);
                writerTGA.Write((ushort)HEIGHT);

                WriteOneByte(writerTGA, 8);
                WriteOneByte(writerTGA, 0);

                for (i = 0; i < 256; i++)
                {
                    WriteOneByte(writerTGA, (lastPal[(i * 3) + 2] * 255) / 0x3F);
                    WriteOneByte(writerTGA, (lastPal[(i * 3) + 1] * 255) / 0x3F);
                    WriteOneByte(writerTGA, (lastPal[(i * 3)] * 255) / 0x3F);
                }

                writerTGA.Write(lastImage);

                writerTGA.Close();

                File.WriteAllBytes(filename + "_" + i_nframe.ToString("0000") + ".TGA",
                                   memTGA.ToArray());
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.Message);
            }
        }


        public static void SaveTGAUnique(string filename, int i_nframe, int i_nchunk)
        {
            int i;
            byte[] tmpPAL = new byte[MAX_PALETTE_BYTES];

            try
            {
                MemoryStream memTGA = new();
                BinaryWriter writerTGA = new(memTGA);

                WriteOneByte(writerTGA, 0);         // size of ID field that follows 18 byte header (0 usually)
                WriteOneByte(writerTGA, 1);         // type of colour map 0=none, 1=has palette
                WriteOneByte(writerTGA, 1);         // type of image 0=none,1=indexed,2=rgb,3=grey,+8=rle packed

                // Palette
                WriteOneByte(writerTGA, 0);         // first colour map entry in palette
                WriteOneByte(writerTGA, 0);

                WriteOneByte(writerTGA, 0);         // number of colours in palette
                WriteOneByte(writerTGA, 1);

                WriteOneByte(writerTGA, 24);        // number of bits per palette entry 15,16,24,32

                WriteOneByte(writerTGA, 0);         // image x origin
                WriteOneByte(writerTGA, 0);

                WriteOneByte(writerTGA, 0);         // image y origin
                WriteOneByte(writerTGA, 0);


                writerTGA.Write((ushort)WIDTH);     // image width in pixels
                writerTGA.Write((ushort)HEIGHT);    // image height in pixels

                WriteOneByte(writerTGA, 8);         // image bits per pixel 8,16,24,32
                WriteOneByte(writerTGA, 0);         // image descriptor bits (vh flip bits)

                for (i = 0; i < 256; i++)
                {
                    WriteOneByte(writerTGA, (lastPal[(i * 3) + 2] * 255) / 0x3F);
                    WriteOneByte(writerTGA, (lastPal[(i * 3) + 1] * 255) / 0x3F);
                    WriteOneByte(writerTGA, (lastPal[(i * 3)] * 255) / 0x3F);
                }

                writerTGA.Write(anim.frames[i_nframe].chunks[i_nchunk].chunkimg);

                writerTGA.Close();

                File.WriteAllBytes(filename + "_" + i_nframe.ToString("0000") +
                                              "_" + i_nchunk.ToString("0000") + ".TGA",
                                   memTGA.ToArray());

            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.Message);
            }
        }

        public static void SaveTGAFrames(string filename, bool bDecodeFull)
        {
            int i_nframe, i_nchunk;

            lastImage = new byte[MAX_IMAGE_SIZE];
            lastPal = new byte[MAX_PALETTE_BYTES];

            for (i_nframe = 0; i_nframe < anim.nframes; i_nframe++)
            {
                if (bDecodeFull) 
                {
                    for (i_nchunk = 0; i_nchunk < anim.frames[i_nframe].nchunks; i_nchunk++)
                    {

                        if (anim.frames[i_nframe].chunks[i_nchunk].chunk_id == 0)
                        {
                            Array.Copy(anim.frames[i_nframe].chunks[i_nchunk].pal,
                                       lastPal, MAX_PALETTE_BYTES);
                        }

                        if (anim.frames[i_nframe].chunks[i_nchunk].bIsImage)
                        {
                            if (anim.frames[i_nframe].chunks[i_nchunk].chunk_size > 0x1000)
                                Array.Copy(anim.frames[i_nframe].chunks[i_nchunk].chunkimg,
                                           lastImage, MAX_IMAGE_SIZE);
                        }
                    }

                    SaveTGAFull(filename, i_nframe);
                }
                else
                {
                    for (i_nchunk = 0; i_nchunk < anim.frames[i_nframe].nchunks; i_nchunk++)
                    {
                        if (anim.frames[i_nframe].chunks[i_nchunk].chunk_id == 0)
                        {
                            Array.Copy(anim.frames[i_nframe].chunks[i_nchunk].pal,
                                       lastPal, MAX_PALETTE_BYTES);
                        }

                        if (anim.frames[i_nframe].chunks[i_nchunk].bIsImage &&
                            anim.frames[i_nframe].chunks[i_nchunk].chunk_size > 2)
                        {
                            SaveTGAUnique(filename, i_nframe, i_nchunk);
                        }
                    }
                }
            }
        }

        public static int GetFrames()
        {
            return anim.frames.Count;
        }


        public static void DumpBinaryData(string filename)
        {
            int i_nframe, i_nchunk, par1, par2;
            List<string> dumpText = [];

            for (i_nframe = 0; i_nframe < anim.nframes; i_nframe++)
            {
                dumpText.Add("Frame: " + i_nframe.ToString("0000"));

                for (i_nchunk = 0; i_nchunk < anim.frames[i_nframe].nchunks; i_nchunk++)
                {
                    switch(anim.frames[i_nframe].chunks[i_nchunk].chunk_id)
                    {
                        case 0:
                            dumpText.Add("Chunk:      " + i_nchunk.ToString("00") + "   ID: " + anim.frames[i_nframe].chunks[i_nchunk].chunk_id.ToString() +
                                         "   Offset:   [" + anim.frames[i_nframe].chunks[i_nchunk].chunk_offset.ToString("X8") + "]" +
                                         "    READ PAL");

                            break;

                        case 1:
                            dumpText.Add("Chunk:      " + i_nchunk.ToString("00") + "   ID: " + anim.frames[i_nframe].chunks[i_nchunk].chunk_id.ToString() +
                                         "   Offset:   [" + anim.frames[i_nframe].chunks[i_nchunk].chunk_offset.ToString("X8") + "]" +
                                         "    CLEAR PAL");

                            break;

                        case 2:
                            dumpText.Add("Chunk:      " + i_nchunk.ToString("00") + "   ID: " + anim.frames[i_nframe].chunks[i_nchunk].chunk_id.ToString() +
                                         "   Offset:   [" + anim.frames[i_nframe].chunks[i_nchunk].chunk_offset.ToString("X8") + "]" +
                                         "    UNPACK INTRA RLE");
                            dumpText.Add("Image Size: " + 
                                anim.frames[i_nframe].chunks[i_nchunk].chunk_size.ToString());



                            break;

                        case 3:
                            dumpText.Add("Chunk:      " + i_nchunk.ToString("00") + "   ID: " + anim.frames[i_nframe].chunks[i_nchunk].chunk_id.ToString() +
                                         "   Offset:   [" + anim.frames[i_nframe].chunks[i_nchunk].chunk_offset.ToString("X8") + "]" +
                                         "    UNPACK INTER RLE");
                            dumpText.Add("Image Size: " +
                                anim.frames[i_nframe].chunks[i_nchunk].chunk_size.ToString());

                            break;

                        case 4:
                            dumpText.Add("Chunk:      " + i_nchunk.ToString("00") + "   ID: " + anim.frames[i_nframe].chunks[i_nchunk].chunk_id.ToString() +
                                         "   Offset:   [" + anim.frames[i_nframe].chunks[i_nchunk].chunk_offset.ToString("X8") + "]" +
                                         "    REPEAT FRAME DATA AT OFFSET");
                            dumpText.Add("Count:      " +
                                (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[0] +
                                (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[1] << 8)).ToString()
                                        );
                            dumpText.Add("Offset:     " +
                                (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[2] +
                                (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[3] << 8) + 
                                (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[4] << 16) +
                                (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[5] << 24)).ToString()
                                        );

                            break;

                        case 5:
                            dumpText.Add("Chunk:      " + i_nchunk.ToString("00") + "   ID: " + anim.frames[i_nframe].chunks[i_nchunk].chunk_id.ToString() +
                                         "   Offset:   [" + anim.frames[i_nframe].chunks[i_nchunk].chunk_offset.ToString("X8") + "]" +
                                         "    COMMAND WITH 2 * 16BIT PARAMETERS");

                            par1 = (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[0] +
                                   (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[1] << 8));
                            par2 = (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[2] +
                                   (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[3] << 8));

                            dumpText.Add("Param 1:    " + par1.ToString() + "[" + par1.ToString("X3") + "]");
                            dumpText.Add("Param 2:    " + par2.ToString() + "[" + par2.ToString("X3") + "]");

                            break;

                        case 6:
                            dumpText.Add("Chunk:      " + i_nchunk.ToString("00") + "   ID: " + anim.frames[i_nframe].chunks[i_nchunk].chunk_id.ToString() +
                                         "   Offset:   [" + anim.frames[i_nframe].chunks[i_nchunk].chunk_offset.ToString("X8") + "]" +
                                         "    COMMAND WITH 1 * 16BIT PARAMETER");

                            par1 = (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[0] +
                                   (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[1] << 8));

                            dumpText.Add("Param 1:    " + par1.ToString() + "[" + par1.ToString("X3") + "]");

                            break;

                        case 7:
                            dumpText.Add("Chunk:      " + i_nchunk.ToString("00") + "   ID: " + anim.frames[i_nframe].chunks[i_nchunk].chunk_id.ToString() +
                                         "   Offset:   [" + anim.frames[i_nframe].chunks[i_nchunk].chunk_offset.ToString("X8") + "]" +
                                         "    COMMAND WITH 2 * 16BIT PARAMETERS (SUBTITLES)");

                            par1 = (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[0] +
                                   (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[1] << 8));
                            par2 = (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[2] +
                                   (anim.frames[i_nframe].chunks[i_nchunk].chunkdata[3] << 8));

                            dumpText.Add("Text Line:  " + par1.ToString("000") + "/[" + par1.ToString("X3") + "]");
                            dumpText.Add("Palette:    " + par2.ToString("000") + "/[" + par2.ToString("X3") + "]");

                            break;

                        case 8:
                            dumpText.Add("Chunk:      " + i_nchunk.ToString("00") + "   ID: " + anim.frames[i_nframe].chunks[i_nchunk].chunk_id.ToString() +
                                         "   Offset:   [" + anim.frames[i_nframe].chunks[i_nchunk].chunk_offset.ToString("X8") + "]" +
                                         "    PUT INTERLACED RLE");
                            dumpText.Add("Image Size: " +
                                anim.frames[i_nframe].chunks[i_nchunk].chunk_size.ToString());

                            break;

                        default:
                            dumpText.Add("Chunk:      " + i_nchunk.ToString("00") + "   ID: " + anim.frames[i_nframe].chunks[i_nchunk].chunk_id.ToString() +
                                         "   Offset:   [" + anim.frames[i_nframe].chunks[i_nchunk].chunk_offset.ToString("X8") + "]" +
                                         "    SKIP CHUNK");
                            break;

                    }
                }

                dumpText.Add("\n");
            }

            File.WriteAllLines(filename + ".TXT", dumpText, Encoding.GetEncoding(1252));
        }

        //static int CountZeros(byte[] chunkimg, int pos)
        //{
        //    int iCount = 0;
        //    bool bFound = false;

        //    while (iCount < 160 && !bFound && pos < chunkimg.Length)
        //    //while (!bFound && pos < chunkimg.Length)
        //    {
        //        if (chunkimg[pos] > 0) bFound = true;
        //        else
        //        {
        //            if (iCount < 160)
        //            {
        //                iCount++;
        //            }

        //            pos++;
        //        }
        //    }

        static int CountByteAppearances(byte[] chunkimg, int pos)
        {
            int iCount = 1;
            byte bValue = chunkimg[pos++];

            bool bFound = false;

            while (iCount < 160 && !bFound && pos < chunkimg.Length)
            //while (!bFound && pos < chunkimg.Length)
            {
                if (chunkimg[pos] != bValue) bFound = true;
                else
                {
                    if (iCount < 160)
                    {
                        iCount++;
                    }

                    pos++;
                }
            }

            return iCount;
        }

        // We must put the package each 640 bytes (640 pixels width)
        // if not, there are issues when showing the cinematic (freezes,
        // incorrect drawing, etc...)
        public static bool PackIntraRLE(ref Chunk tmpChunk, string filename)
        {
            int pos, posdata, i, len, stackWIDTH;
            byte[] tmpData = new byte[MAX_IMAGE_SIZE];

            pos = 0; 
            stackWIDTH = 0;
            posdata = 0;

            while (pos < tmpChunk.chunkimg.Length)
            {

                while (stackWIDTH < WIDTH)
                {
                    
                    len = CountByteAppearances(tmpChunk.chunkimg, pos + stackWIDTH);

                    if (len == 1)
                    {
                        Console.WriteLine(
                            "WARNING: The length of consecutive pixels with same color is of 1.\n" +
                            "That should NOT happen. All the images must have a pixelation of 2 consecutive\n" +
                            "pixels with same color or paired number. The pixel detected with this\n" +
                            "behaviour is at location X/Y of pixel " +
                            "[ X: " + stackWIDTH.ToString() + " , " +
                            "Y: " + (pos / 640).ToString() + " ].\n" +
                            "The file: " + filename + " has been skipped.\n" +
                            "Please, check again the image.\n"
                                         );

                        return false;
                    }

                    if (len == 0) len = 16;

                    if (stackWIDTH + len > WIDTH)
                    {
                        len = WIDTH - stackWIDTH;
                    }

                    if (len == 160)
                    {
                        tmpData[posdata] = 0xB1;
                        posdata++;
                        tmpData[posdata] = 0x00;
                        posdata++;
                    }
                    else if (len > 16)
                    {
                        tmpData[posdata] = (byte)(257 - (len / 2));
                        posdata++;
                        tmpData[posdata] = 0x00;
                        posdata++;
                    }
                    else
                    {
                        tmpData[posdata] = (byte)((len / 2) - 1);
                        posdata++;

                        for (i = stackWIDTH; i < stackWIDTH + len; i += 2)
                        {
                            tmpData[posdata] = tmpChunk.chunkimg[pos + i];
                            posdata++;
                        }
                    }

                    stackWIDTH += len;
                }

                pos += stackWIDTH;
                stackWIDTH = 0;

            }

            tmpChunk.chunkdata = new byte[posdata];
            Array.Copy(tmpData, 0, tmpChunk.chunkdata, 0, posdata);
            tmpChunk.chunk_size = posdata;

            return true;
        }

        public static void ImportTGA2ANM(string filename, string[] importFiles)
        {
            Chunk tmpChunk = new Chunk();
            int i_nframe, i_nchunk;

            foreach (string importFile in importFiles)
            {
                if (File.Exists(importFile))
                {
                    i_nframe = Int32.Parse(importFile.Split('_')[1]);
                    i_nchunk = Int32.Parse(importFile.Split('_')[2].Split('.')[0]);

                    if (anim.frames[i_nframe].chunks[i_nchunk].bIsImage)
                    {
                        tmpChunk.chunkimg = new byte[MAX_IMAGE_SIZE];

                        Array.Copy(File.ReadAllBytes(importFile), 0x312, tmpChunk.chunkimg, 0, WIDTH * HEIGHT);
                        tmpChunk.chunkimg = Reverse(tmpChunk.chunkimg);

                        if (PackIntraRLE(ref tmpChunk, importFile))
                        {
                            tmpChunk.chunk_id = 2;
                            tmpChunk.bIsImage = anim.frames[i_nframe].chunks[i_nchunk].bIsImage;

                            anim.frames[i_nframe].chunks[i_nchunk] = tmpChunk;
                        }
                    }
                    else
                    {
                        Console.WriteLine("The file: " + importFile + " can not be put at\n");
                        Console.WriteLine("Frame: " + i_nframe.ToString() +
                                          ", Chunk: " + i_nchunk.ToString() +
                                          " because it is not a valid image frame/chunk.\n");
                    }
                }
            }

            SaveAnimation(filename);
        }

        //public static void ImportTGA2ANM(string filename, string[] importFiles)
        //{
        //    Chunk tmpChunk = new Chunk();
        //    int i_nframe, i_nchunk;

        //    foreach (string importFile in importFiles)
        //    {
        //        if (File.Exists(importFile))
        //        {
        //            i_nframe = Int32.Parse(importFile.Split('_')[1]);
        //            i_nchunk = Int32.Parse(importFile.Split('_')[2].Split('.')[0]);

        //            if (anim.frames[i_nframe].chunks[i_nchunk].bIsImage)
        //            {
        //                tmpChunk.chunkimg = new byte[MAX_IMAGE_SIZE];

        //                Array.Copy(File.ReadAllBytes(importFile), 0x312, tmpChunk.chunkimg, 0, WIDTH * HEIGHT);

        //                PackIntraRLE(ref tmpChunk);

        //                //tmpChunk.chunk_id = anim.frames[i_nframe].chunks[i_nchunk].chunk_id;
        //                tmpChunk.chunk_id = 2;
        //                tmpChunk.bIsImage = anim.frames[i_nframe].chunks[i_nchunk].bIsImage;

        //                anim.frames[i_nframe].chunks[i_nchunk] = tmpChunk;
        //            }
        //            else
        //            {
        //                Console.WriteLine("The file: " + importFile + " can not be put at\n");
        //                Console.WriteLine("Frame: " + i_nframe.ToString() +
        //                                  ", Chunk: " + i_nchunk.ToString() +
        //                                  " because it is not a valid image frame/chunk.\n");
        //            }
        //        }
        //    }

        //    SaveAnimation(filename);
        //}

        public static void SaveAnimation(string filename)
        {
            int framecnt = -1;
            
            try
            {
                MemoryStream memAnm = new();
                BinaryWriter writerAnm = new(memAnm);

                writerAnm.Write(anim.nframes);
                writerAnm.Write(anim.version);

                foreach (Frame tmpFrame in anim.frames)
                {
                    framecnt++;
                    writerAnm.Write(tmpFrame.nchunks);

                    foreach (Chunk tmpChunk in tmpFrame.chunks)
                    {
                        writerAnm.Write(tmpChunk.chunk_id);
                        writerAnm.Write(tmpChunk.chunk_size);

                        switch (tmpChunk.chunk_id)
                        {
                            case 0:
                                writerAnm.Write(tmpChunk.pal);

                                break;

                            default:
                                writerAnm.Write(tmpChunk.chunkdata);

                                break;
                        }
                    }
                }

                memAnm.Close();

                File.WriteAllBytes(filename, memAnm.ToArray());

            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: " + e.Message);
            }

        }
    }
}
