using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StackAnalyser
{
    class AsmFunction
    {
        static bool IncludeTotal { get; set; } = true;
        static bool IncludeUsed { get; set; } = true;
        static int ShortNameLength { get; set; } = 30;
        static bool MultiLine { get; set; } = true;

        public int ManuallyReserved { get; set; }

        private string _name;
        public string Name 
        { 
            get => _name;
            set
            {
                _name = value;
                if (_name.Length < ShortNameLength)
                {
                    ShortName = _name;
                    return;
                }
                var m = Regex.Match(value, @"([^\d]*\d+){2}");
                int trimStart = 0;
                if (m.Success)
                    trimStart = m.Length;
                if (trimStart + ShortNameLength > value.Length)
                    trimStart = value.Length - ShortNameLength;
                ShortName = value.Substring(trimStart, ShortNameLength);
            }
        }
        public string ShortName { get; private set; }
        public int StartAddress { get; set; }
        public int StackUsed { get; set; }
        public HashSet<int> CallAddresses { get; } = new HashSet<int>();
        public HashSet<AsmFunction> Callees { get; set; }
        public List<string> Lines { get; set; } = new List<string>();
        public int LineStart { get; set; }

        const int retBytes = 2; //The byes necessary to store a return address on the stack (because call is much more common than JMP between functions)
        public IEnumerable<(string desc, int stackUsed)> GetCallGraphs(
            HashSet<AsmFunction> visited,
            int curStackSize = 0)
        {
            try
            {
                curStackSize += StackUsed + 2;
                string totalStr = IncludeTotal ? $" = {curStackSize}" : "";
                string stackStr = IncludeUsed ? $" (+{StackUsed + 2}{totalStr})" : "";
                var size = Math.Min(ShortNameLength, Name.Length);
                int start = 0;
                while (start + size < Name.Length && (Name[start] > '9' || Name[start] < '0'))
                    start++;
                while (start + size < Name.Length && (Name[start] < '9' || Name[start] > '0'))
                    start++;

                string thisFuncStr = ShortName + stackStr;
                string separator = MultiLine ? Environment.NewLine + "       " : " >> ";
                if (!visited.Add(this))
                    yield return ($"{thisFuncStr} <RECUSION>", StackUsed);
                else
                {
                    bool any = false;
                    /*if (MultiLine)
                        yield return ($"{thisFuncStr}", StackUsed);*/
                    foreach (var calleeGraph in Callees.SelectMany(f => f.GetCallGraphs(visited, curStackSize)))
                    {
                        any = true;
                        /*if (MultiLine)
                            yield return (" " + calleeGraph.desc, calleeGraph.stackUsed + StackUsed);
                        else*/
                        yield return ($"{thisFuncStr}{separator}{calleeGraph.desc}", calleeGraph.stackUsed + retBytes + StackUsed);
                    }
                    if (!any)
                        yield return ($"{thisFuncStr}", StackUsed);
                }
            }
            finally
            {
                visited.Remove(this);
            }
        }

        public override string ToString()
        {
            return $"{Name} (stack: {StackUsed} start: {StartAddress:X} length: {Lines.Count})";
        }
    }
}
