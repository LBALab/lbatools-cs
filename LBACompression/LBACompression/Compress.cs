using System;

namespace LBACompression
{
    public class CompressLBA
    {
        public sbyte[] Src { get; set; }
        public byte[] Dst { get; set; }
        public int SrcLen { get; set; }
        public int DstLen { get; set; }

        public uint MinBlock = 2;

        private const int RecoverArea = 512;

        public byte[] GetDst()
        {
            return Dst;
        }

        public int GetDstLen()
        {
            return DstLen;
        }

        public void Compress()
        {
            DstLen = -2;
        }

        public void Decompress()
        {
            int I = 0, J = 0;
            int K = 0;

            while (I <= Src.GetUpperBound(0)) {
                byte Bits;
                byte Type = (byte) Src[I++];

                for (Bits = 1; Bits != 0; Bits <<= 1)
                {
                    if ((Type & Bits) != 0)
                    {
                        Dst[J++] = (byte) Src[I++];
                    }
                    else
                    {
                        int Offset = ((int) Src[I]) | ((int) (Src[I + 1] << 8));
                        int Length = (Offset & 0x0f) + (int)MinBlock;
                        int Ptr;

                        I += 2;
                        Offset >>= 4;

                        Ptr = J - Offset - 1;

                        if (Offset == 0)
                        {
                            for (K = 0; K < Length; K++)
                                Dst[J + K] = Dst[Ptr];
                        }
                        else
                        {
                            if ((Ptr + Length) >= J)
                            {
                                int N;
                                int Temp = J;
                                for (N = Length; N > 0; N--)
                                    Dst[Temp++] = Dst[Ptr++];
                            }
                            else
                            {
                                for (K = 0; K < Length; K++)
                                    Dst[J + K] = Dst[Ptr + K];
                            }
                        }

                        J += Length;
                    }

                    if (I >= Src.GetUpperBound(0))
                    {
                        DstLen = J + 1;
                        return;
                    }
                }
            }
        }

        public CompressLBA(byte[] SrcBuf, int SrcLength)
        {
            int I;

            /* This is needed because we need some overhead or the LZMIT code can crash due to
             * going out of bounds.
             */
            Src = new sbyte[SrcBuf.GetLength(0) + (1 << 5)];
            for (I = 0; I < Src.GetLength(0); I++)
                Src[I] = 0x00;
            Array.Copy(SrcBuf, Src, SrcBuf.GetLength(0));

            SrcLen = SrcLength;

            Dst = new byte[SrcLen * 2];
        }
    }

    public class CompressLZSS : CompressLBA
    {
        private const int IndexBitCount = 12;
        private const int LengthBitCount = 4;
        private const int WindowSize = (1 << 12);
        private const int RawLookAheadSize = (1 << 4);
        private const int BreakEven = ((1 + 12 + 4) / 9);
        private const int LookAheadSize = (1 << 4) + ((1 + 12 + 4) / 9);
        private const int TreeRoot = (1 << 12);
        private const int Unused = -1;

        struct DefTree
        {
            public int Parent;
            public int SmallerChild;
            public int LargerChild;
        }

        private byte[] Window;
        private DefTree[] LZTree;

        private int MatchPos;

        private int ModWindow(int A)
        {
            return A & (WindowSize - 1);
        }

        private void InitTree(int R)
        {
            int I;

            for (I = 1; I <= WindowSize + 1; I++)
            {
                LZTree[I].Parent = Unused;
                LZTree[I].LargerChild = Unused;
                LZTree[I].SmallerChild = Unused;
            }

            LZTree[TreeRoot + 1].LargerChild = R;
            LZTree[R + 1].Parent = TreeRoot;
        }

        private void ContractNode(int OldNode, int NewNode)
        {
            LZTree[NewNode + 1].Parent = LZTree[OldNode + 1].Parent;
            if (LZTree[LZTree[OldNode + 1].Parent + 1].LargerChild == OldNode)
                LZTree[LZTree[OldNode + 1].Parent + 1].LargerChild = NewNode;
            else
                LZTree[LZTree[OldNode + 1].Parent + 1].SmallerChild = NewNode;
            LZTree[OldNode + 1].Parent = Unused;
        }

        private void ReplaceNode(int OldNode, int NewNode)
        {
            int Parent;

            Parent = LZTree[OldNode + 1].Parent;
            if (LZTree[Parent + 1].SmallerChild == OldNode)
                LZTree[Parent + 1].SmallerChild = NewNode;
            else
                LZTree[Parent + 1].LargerChild = NewNode;
            LZTree[NewNode + 1] = LZTree[OldNode + 1];
            if (LZTree[NewNode + 1].SmallerChild != Unused)
                LZTree[LZTree[NewNode + 1].SmallerChild + 1].Parent = NewNode;
            if (LZTree[NewNode + 1].LargerChild != Unused)
                LZTree[LZTree[NewNode + 1].LargerChild + 1].Parent = NewNode;
            LZTree[OldNode + 1].Parent = Unused;
        }

        private int FindNextNode(int Node)
        {
            int Next;

            Next = LZTree[Node + 1].SmallerChild;
            while (LZTree[Next + 1].LargerChild != Unused)
                Next = LZTree[Next + 1].LargerChild;

            return Next;
        }

        private void DeleteString(int P)
        {
            int Replacement;

            if (LZTree[P + 1].Parent == Unused)
                return;
            if (LZTree[P + 1].LargerChild == Unused)
                ContractNode(P, LZTree[P + 1].SmallerChild);
            else if (LZTree[P + 1].SmallerChild == Unused)
                ContractNode(P, LZTree[P + 1].LargerChild);
            else
            {
                Replacement = FindNextNode(P);
                DeleteString(Replacement);
                ReplaceNode(P, Replacement);
            }
        }

        private bool IsChildUnused(int Delta, int TestNode)
        {
            if (Delta >= 0)
                return (LZTree[TestNode + 1].LargerChild == Unused);
            else
                return (LZTree[TestNode + 1].SmallerChild == Unused);
        }

        private void SetNewNode(int Delta, int TestNode, int NewNode)
        {
            if (Delta >= 0)
                LZTree[TestNode + 1].LargerChild = NewNode;
            else
                LZTree[TestNode + 1].SmallerChild = NewNode;
        }

        private int GetChild(int Delta, int TestNode)
        {
            if (Delta >= 0)
                return LZTree[TestNode + 1].LargerChild;
            else
                return LZTree[TestNode + 1].SmallerChild;
        }

        private int AddString(int NewNode)
        {
            int I, TestNode, Delta = 0, MatchLength;

            TestNode = LZTree[TreeRoot + 1].LargerChild;
            MatchLength = 0;

            for ( ; ; )
            {
                for (I = 0; I < LookAheadSize; I++)
                {
                    Delta = (int)Window[ModWindow(NewNode + I)] - (int)Window[ModWindow(TestNode + I)];
                    if (Delta != 0)
                        break;
                }

                if (I >= MatchLength)
                {
                    MatchLength = I;
                    MatchPos = TestNode;

                    if (MatchLength >= LookAheadSize)
                    {
                        ReplaceNode(TestNode, NewNode);
                        return MatchLength;
                    }
                }

                if (IsChildUnused(Delta, TestNode))
                {
                    SetNewNode(Delta, TestNode, NewNode);
                    LZTree[NewNode + 1].Parent = TestNode;
                    LZTree[NewNode + 1].LargerChild = Unused;
                    LZTree[NewNode + 1].SmallerChild = Unused;
                    return MatchLength;
                }

                TestNode = GetChild(Delta, TestNode);
            }
        }

        new public void Compress()
        {
            int I, J = 0, K = 0;
            int Info = 0, LookAheadBytes;
            int ReplaceCount, MatchLength = 0;
            int CountBits = 0;
            int NewNode = 0;
            short Temp = 0;
            sbyte Mask = 1;
            int Len = 0, SaveLength = SrcLen;

            MatchPos = 0;

            for (I = 0; I < LookAheadSize; I++)
            {
                if (SrcLen == 0)
                    break;

                Window[NewNode + I] = (byte) Src[J++];
                SrcLen--;
            }

            LookAheadBytes = I;
            InitTree(NewNode);
            Info = K++;

            if (++Len >= SaveLength)
            {
                DstLen = SaveLength;
                return;
            }

            Dst[Info] = 0;

            while (LookAheadBytes > 0)
            {
                if (MatchLength > LookAheadBytes)
                    MatchLength = LookAheadBytes;

                if (MatchLength <= BreakEven)
                {
                    ReplaceCount = 1;
                    Dst[Info] |= (byte) Mask;
                    Dst[K++] = Window[NewNode];
                    if (++Len >= SaveLength)
                    {
                        DstLen = SaveLength;
                        return;
                    }
                }
                else
                {
                    if ((Len = Len + 2) >= SaveLength)
                    {
                        DstLen = SaveLength;
                        return;
                    }

                    Temp = (short)((ModWindow(NewNode - MatchPos - 1) << LengthBitCount) |
                                  (MatchLength - BreakEven - 1));
                    Dst[K] = (byte) (Temp & 0xff);
                    Dst[K + 1] = (byte) (Temp >> 8);

                    K += 2;
                    ReplaceCount = MatchLength;
                }

                if (++CountBits == 8)
                {
                    if (++Len >= SaveLength)
                    {
                        DstLen = SaveLength;
                        return;
                    }

                    Info = K++;
                    Dst[Info] = 0;
                    CountBits = 0;
                    Mask = 1;
                }
                else
                    Mask = (sbyte)(Mask << 1);

                for (I = 0; I < ReplaceCount; I++)
                {
                    DeleteString(ModWindow(NewNode + LookAheadSize));
                    if (SrcLen == 0)
                        LookAheadBytes--;
                    else
                    {
                        Window[ModWindow(NewNode + LookAheadSize)] = (byte) Src[J++];
                        SrcLen--;
                    }

                    NewNode = ModWindow(NewNode + 1);
                    if (LookAheadBytes != 0)
                        MatchLength = AddString(NewNode);
                }
            }

            if (CountBits == 0)
                Len--;

            DstLen = Len;
        }

        public CompressLZSS(byte[] src_buf, int src_length) : base(src_buf, src_length)
        {
            MinBlock = 2;

            Window = new byte[WindowSize * 5];
            /* +1 because we shift all offsets to avoid -1 entries. */
            LZTree = new DefTree[WindowSize + 2 + 1];

            MatchPos = 0;
        }
    }

    public class CompressLZMIT : CompressLBA
    {
        private const int IndexBitCount = 12;
        private const int LengthBitCount = 4;
        private const int RawLookAheadSize = (1 << 4);
        private const int MaxOffset = ((1 << 12) + 1);
        private const int TreeRoot = ((1 << 12) + 1);
        private const int Unused = -1;
        private const int Smaller = 0;
        private const int Larger = 1;

        struct LZMITTree
        {
            public int SmallerChild;
            public int LargerChild;
            public int Parent;
            public int WhichChild;
        }

        private LZMITTree[] LZTree;

        private void ReplaceParents(int Node)
        {
            LZTree[LZTree[Node + 1].SmallerChild + 1].Parent = Node;
            LZTree[LZTree[Node + 1].LargerChild + 1].Parent = Node;
        }

        private void ReplaceNode(int OldNode, int NewNode)
        {
            LZTree[NewNode + 1] = LZTree[OldNode + 1];

            ReplaceParents(NewNode);

            if (LZTree[OldNode + 1].WhichChild == 0)
                LZTree[LZTree[OldNode + 1].Parent + 1].SmallerChild = NewNode;
            else
                LZTree[LZTree[OldNode + 1].Parent + 1].LargerChild = NewNode;
        }

        private void UpdateParent(int Node, int Parent, int WhichChild)
        {
            LZTree[Node + 1].Parent = Parent;
            LZTree[Node + 1].WhichChild = WhichChild;
        }

        private int FindNextNode(int Node)
        {
            int Next = LZTree[Node + 1].SmallerChild;

            if (LZTree[Next + 1].LargerChild == Unused)
                LZTree[Node + 1].SmallerChild = LZTree[Next + 1].SmallerChild;
            else
            {
                while (LZTree[Next + 1].LargerChild != Unused)
                    Next = LZTree[Next + 1].LargerChild;
                LZTree[LZTree[Next + 1].Parent + 1].LargerChild = LZTree[Next + 1].SmallerChild;
            }

            return Next;
        }

        private void UpdateChild(int SrcTree, int WhichChild)
        {
            int Child;

            if (WhichChild == 0)
                Child = LZTree[SrcTree + 1].SmallerChild;
            else
                Child = LZTree[SrcTree + 1].LargerChild;

            if (Child != Unused)
                UpdateParent(Child, LZTree[SrcTree + 1].Parent, LZTree[SrcTree + 1].WhichChild);

            if (LZTree[SrcTree + 1].WhichChild == 0)
                LZTree[LZTree[SrcTree + 1].Parent + 1].SmallerChild = Child;
            else
                LZTree[LZTree[SrcTree + 1].Parent + 1].LargerChild = Child;
        }

        new public void Compress()
        {
            int Val, Temp, SrcOff, OutLen, OffsetOff, FlagBit, BestMatch = 1, BestNode = 0;
            int CurNode, Node, I, J, Replacement, CmpString, CurString, SrcTree, Diff;

            for (I = 1; I <= (MaxOffset + 1); I++)
            {
                LZTree[I].SmallerChild = Unused;
                LZTree[I].LargerChild = Unused;
                LZTree[I].Parent = Unused;
                LZTree[I].WhichChild = Unused;
            }

            SrcOff = FlagBit = OffsetOff = Val = 0;
            BestMatch = OutLen = 1;

            while ((BestMatch + SrcOff - 1) < SrcLen)
            {
                I = BestMatch;
                while (I > 0)
                {
                    SrcTree = SrcOff % MaxOffset;

                    if (LZTree[SrcTree + 1].Parent != Unused)
                    {
                        if ((LZTree[SrcTree + 1].SmallerChild != Unused) && (LZTree[SrcTree + 1].LargerChild != Unused))
                        {
                            Replacement = FindNextNode(SrcTree);
                            if (LZTree[Replacement + 1].WhichChild == 0)
                                UpdateParent(LZTree[Replacement + 1].SmallerChild, LZTree[Replacement + 1].Parent, Smaller);
                            else
                                UpdateParent(LZTree[Replacement + 1].SmallerChild, LZTree[Replacement + 1].Parent, Larger);
                            ReplaceNode(SrcTree, Replacement);
                        }
                        else
                            UpdateChild(SrcTree, (LZTree[SrcTree + 1].SmallerChild == Unused) ? Larger : Smaller);
                    }

                    LZTree[SrcTree + 1].LargerChild = LZTree[SrcTree + 1].SmallerChild = Unused;

                    CurNode = LZTree[TreeRoot + 1].SmallerChild;

                    if (CurNode < 0)
                    {
                        BestMatch = BestNode = 0;

                        UpdateParent(SrcTree, TreeRoot, 0);
                        LZTree[TreeRoot + 1].SmallerChild = SrcTree;
                    }
                    else
                    {
                        BestMatch = 2;

                        while (true)
                        {
                            CurString = SrcOff;
                            CmpString = CurString - ((SrcTree - CurNode + MaxOffset) % MaxOffset);
                            Node = CurNode;
                            J = RawLookAheadSize + 2;
                            CurNode = J - 1;

                            do
                                Diff = (int)(Src[CurString++]) - (int)(Src[CmpString++]);
                            while ((--J != 0) && (Diff == 0));

                            if ((J != 0) || (Diff != 0))
                            {
                                CurNode -= J;
                                if (CurNode > BestMatch)
                                {
                                    BestMatch = CurNode;
                                    BestNode = Node;
                                }

                                J = (Diff >= 0) ? Larger : Smaller;
                                if (J == 0)
                                    CurNode = LZTree[Node + 1].SmallerChild;
                                else
                                    CurNode = LZTree[Node + 1].LargerChild;

                                if (CurNode < 0)
                                {
                                    UpdateParent(SrcTree, Node, J);
                                    if (J == 0)
                                        LZTree[Node + 1].SmallerChild = SrcTree;
                                    else
                                        LZTree[Node + 1].LargerChild = SrcTree;
                                    break;
                                }
                            }
                            else
                            {
                                ReplaceNode(Node, SrcTree);
                                LZTree[Node + 1].Parent = Unused;
                                BestMatch = (RawLookAheadSize + 2);
                                BestNode = Node;
                                break;
                            }
                        }
                    }

                    if (--I > 0)
                        SrcOff++;
                }

                if (OutLen >= (SrcLen - RawLookAheadSize - 1))
                {
                    OutLen = -1;
                    break;
                }

                Val >>= 1;

                if ((BestMatch > 2) && (SrcOff + BestMatch <= SrcLen))
                {
                    Temp = (BestMatch - 3) | (((SrcOff - BestNode - 1 + MaxOffset) % MaxOffset) << LengthBitCount);
                    Dst[OutLen] = (byte) (Temp & 0xff);
                    Dst[OutLen + 1] = (byte) (Temp >> 8);
                    OutLen += 2;
                }
                else
                {
                    Dst[OutLen++] = (byte) Src[SrcOff];
                    Val |= 0x80;
                    BestMatch = 1;
                }

                FlagBit++;
                if (FlagBit >= 8)
                {
                    FlagBit = 0;
                    Dst[OffsetOff] = (byte) (Val & 0xff);
                    OffsetOff = OutLen;
                    OutLen++;
                }

                SrcOff++;
            }

            if (FlagBit == 0)
                OutLen--;
            else if (FlagBit < 8)
                Dst[OffsetOff] = (byte) (Val >> (8 - FlagBit));

            DstLen = OutLen;
        }

        public CompressLZMIT(byte[] src_buf, int src_length) : base(src_buf, src_length)
        {
            MinBlock = 3;
            
            LZTree = new LZMITTree[MaxOffset + 2];
        }
    }
}
