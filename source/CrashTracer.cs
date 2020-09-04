using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace StackAnalyser
{
    class CrashTracer
    {
        public List<AsmFunction> Functions { get; private set; }
        public List<(AsmFunction func, int retAddress)> StackTrace { get; private set; } = new List<(AsmFunction func, int retAddress)>();
        public CrashTracer(IEnumerable<AsmFunction> functions)
        {
            Functions = functions.OrderBy(f => f.StartAddress).ToList();
        }

        public const int StackEnd = 0x900;

        public int StackPointer { get; private set; }

        private static byte[] ConvertToBytes(string trace)
        {
            if (trace.Length % 2 != 0)
                throw new InvalidDataException("ConvertToBytes requires an even number of characters because 2 hex chars per byte.");
            byte[] ret = new byte[trace.Length / 2];
            for (int i = 0; i < ret.Length; i++)
                ret[i] = byte.Parse(trace.AsSpan(i * 2, 2), NumberStyles.HexNumber);
            return ret;
        }

        public void Trace(string trace)
        {
            Trace(ConvertToBytes(trace));
        }

        public void Trace(byte[] stackDump)
        {
            var SPL = stackDump[0];
            var SPH = stackDump[1];
            StackPointer = SPH * 0x100 + SPL;
            int curLoc = 2;
            while (curLoc + 2 <= stackDump.Length)
            {
                var curAddress = (stackDump[curLoc] * 256 + stackDump[curLoc + 1]) * 2;
                var curFunc = Functions.FindLast(f => f.StartAddress < curAddress);
                
                var partFunc = new Analyser().AnalysePart(curFunc, curAddress);
                StackTrace.Add((partFunc, curAddress));

                //2 bytes for the ret address
                curLoc += 2 + partFunc.StackUsed;
            }
        }
    }
}
