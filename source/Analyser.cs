using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StackAnalyser
{
    class Analyser
    {
        const string lineStart = @"^\s*(?<address>[\da-fA-F]+):(\s+[\da-fA-F]{2}){2,4}\s+";

        static readonly Regex _asmLine = new Regex(lineStart, RegexOptions.Compiled);
        static readonly Regex _fnStart = new Regex(@"^(?<address>[\da-fA-F]+) <(?<name>[\w\.]+)>:$", RegexOptions.Compiled);
        static readonly Regex _pusher = new Regex(lineStart + @"push\s+r\d+$", RegexOptions.Compiled);
        static readonly Regex _prologueSaves = new Regex(lineStart + @"r?jmp\s+0x[\da-fA-F]+\s*;\s*0x[\da-fA-F]+\s+<__prologue_saves__(\+0x(?<offset>[\da-fA-F]+))?>", RegexOptions.Compiled);
        static readonly Regex _prePrologueReserve = new Regex(lineStart + @"ldi\s+r(?<register>2(6|7)),\s+0x[\da-fA-F]+\s*;\s*(?<amount>\d+)", RegexOptions.Compiled);
        static readonly Regex _caller = new Regex(lineStart + @"r?call\s+((0x[\da-fA-F]+)|(\.(\+|-)\d+))\s*;\s*0x(?<dest>[\da-fA-F]+)", RegexOptions.Compiled);
        static readonly Regex _selfCall = new Regex(lineStart + @"rcall\s+\.\+0", RegexOptions.Compiled);
        static readonly Regex _outsideJump = new Regex(lineStart + @"r?jmp\s+((0x[\da-fA-F]+)|(\.(\+|-)\d+))\s*;\s*0x(?<dest>[\da-fA-F]+)\s*<(?<destName>[\w\.]+)(\+0x(?<offset>[\da-fA-F]+))?>", RegexOptions.Compiled);

        //For stack dump analysis:
        static readonly Regex _popRegex = new Regex(lineStart + @"pop\s+r\d+$", RegexOptions.Compiled);
        static readonly Regex _epilogueRestores = new Regex(lineStart + @"r?jmp\s+0x[\da-fA-F]+\s*;\s*0x[\da-fA-F]+\s+<__epilogue_restores__(\+0x(?<offset>[\da-fA-F]+))?>", RegexOptions.Compiled);
        static readonly Regex _releaseRegex = new Regex(lineStart + @"(adiw?\s+r28,\s+0x[\da-fA-F]+)\s*;\s*(?<amt>\d+)", RegexOptions.Compiled);

        static readonly Regex _reserveRegex = new Regex(lineStart + @"su?biw?\s+r28,\s+0x[\da-fA-F]+\s*;\s*(?<amt>\d+)", RegexOptions.Compiled);
        static readonly Regex _reserveRegex2 = new Regex(lineStart + @"sbc\s+r29,\s+r1");
        static readonly Regex _reserveRegexHi = new Regex(lineStart + @"sbci\s+r29,\s+0x[\da-fA-F]+\s*;\s*(?<amt>\d+)", RegexOptions.Compiled);
        static readonly Regex[] _reserveFollowing = new[]
        {
            new Regex(lineStart + @"in\s+r0,\s+0x3f", RegexOptions.Compiled),
            new Regex(lineStart + @"cli", RegexOptions.Compiled),
            new Regex(lineStart + @"out\s+0x3e,\s+r29", RegexOptions.Compiled)
        };

        public List<AsmFunction> Functions => _functions.Values.OrderBy(f => f.Name).ToList();
        public List<AsmFunction> Roots { get; private set; }

        AsmFunction _curFunction;
        Dictionary<int, AsmFunction> _functions = new Dictionary<int, AsmFunction>();
        IList<string> _lines;
        int _curIdx;
        int _curAddress;
        int _endAddressExclusive = int.MaxValue;
        string Line => _lines[_curIdx];

        public void Analyse(IList<string> lines)
        {
            _curIdx = 0;
            _lines = lines;
            while (AnalyseLine(false))
            {
                _curIdx++;
            }
            foreach (var func in _functions.Values)
            {
                foreach (var missingAddress in func.CallAddresses.Where(a => !_functions.ContainsKey(a)))
                    Console.Error.WriteLine($"Missing function {missingAddress:X} in {func.Name}");
                func.Callees = func.CallAddresses
                    .Select(i => _functions.TryGetValue(i, out var ret) ? ret : null)
                    .Where(f => f != null)
                    .ToHashSet();
            }
            Roots = _functions.Values.Where(f => !_functions.Values.Any(f2 => f2.Callees.Contains(f))).ToList();
        }

        public AsmFunction AnalysePart(AsmFunction function, int retAddress)
        {
            /*_curFunction = new AsmFunction()
            {
                Name = function.Name,
                StartAddress = function.StartAddress
            };*/
            _curIdx = 0;
            _curAddress = function.StartAddress;
            _lines = function.Lines;
            _endAddressExclusive = retAddress;
            while (AnalyseLine(true))
            {
                _curIdx++;
            }
            return _curFunction;
        }

        bool AnalyseLine(bool allowRelease)
        {
            try
            {
                if (!CanContinue())
                    return false;
                var ret = TryFunctionStart();
                if (_curFunction != null)
                {
                    ret = ret || TryPusher();
                    ret = ret || TrySelfCall();
                    ret = ret || TryPrologue();
                    ret = ret || TryCall();
                    ret = ret || TryReserve(false);
                    ret = ret || TryOutsideJump();
                    if (allowRelease)
                    {
                        ret = ret || TryPop();
                        ret = ret || TryReserve(true);
                        ret = ret || TryEpilogue();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error {ex.Message}\r\n {_curIdx} {Line}");
            }

            if (_curFunction != null)
                _curFunction.Lines.Add(Line);

            return true;
        }

        bool CanContinue()
        {
            if (_curIdx >= _lines.Count)
                return false;
            var m = _asmLine.Match(Line);
            if (!m.Success) //It's a piece of code interspersed with the assembly
                return true;
            _curAddress = int.Parse(m.Groups["address"].Value, NumberStyles.HexNumber);
            if (_curAddress >= _endAddressExclusive)
                return false;
            return true;
        }

        bool TryFunctionStart()
        {
            var m = _fnStart.Match(Line);
            if (!m.Success)
                return false;
            _curFunction = new AsmFunction()
            {
                Name = m.Groups["name"].Value,
                StartAddress = int.Parse(m.Groups["address"].Value, System.Globalization.NumberStyles.HexNumber),
                LineStart = _curIdx
            };
            _functions.Add(_curFunction.StartAddress, _curFunction);
            return true;
        }

        bool TryPrologue()
        {
            if (_curIdx > 4288)
            { }
            var m = _prologueSaves.Match(Line);
            if (!m.Success)
                return false;
            int? r26 = null;
            int? r27 = null;
            for (int i = _curFunction.LineStart + 1; i < _curIdx && (!r26.HasValue || !r27.HasValue); i++)
            {
                var test = _prePrologueReserve.Match(_lines[i]);
                if (!test.Success)
                    continue;
                int val = int.Parse(test.Groups["amount"].Value);
                switch (test.Groups["register"].Value)
                {
                    case "26":
                        Debug.Assert(r26 == null);
                        r26 = val;
                        break;
                    case "27":
                        Debug.Assert(r27 == null);
                        r27 = val;
                        break;
                    default:
                        throw new Exception("Unexpected register");
                }
            }
            int offset = 
                m.Groups["offset"].Success ? int.Parse(m.Groups["offset"].Value, NumberStyles.HexNumber) : 0;
            int reserve = r26.Value + r27.Value * 256 + (18 - offset / 2);
            _curFunction.StackUsed += reserve;
            _curFunction.ManuallyReserved = reserve;
            return true;
        }

        bool TryPusher()
        {
            var m = _pusher.Match(Line);
            if (!m.Success)
                return false;
            _curFunction.StackUsed++;
            return true;
        }

        bool TrySelfCall()
        {
            var m = _selfCall.Match(Line);
            if (!m.Success)
                return false;
            _curFunction.StackUsed += 2;
            return true;
        }

        bool TryCall()
        {
            var m = _caller.Match(Line);
            if (!m.Success)
                return false;
            int dest = int.Parse(m.Groups["dest"].Value, NumberStyles.HexNumber);
            _curFunction.CallAddresses.Add(dest);
            return true;
        }

        bool TryReserve(bool canRelease)
        {
            bool isRelease = false;
            var m = _reserveRegex.Match(Line);
            if (!m.Success)
            {
                if (canRelease)
                {
                    m = _releaseRegex.Match(Line);
                    isRelease = true;
                }
                if (!m.Success)
                    return false;
            }
            int i = _curIdx + 1;
            int hiAmount = 0;

            while (CanContinue() && !_asmLine.IsMatch(_lines[i]))
                i++;

            if (CanContinue())
            {
                if (_reserveRegex2.IsMatch(_lines[i]))
                    i++;
                else if (!isRelease)
                {
                    var hiMatch = _reserveRegexHi.Match(_lines[i]);
                    if (hiMatch.Success)
                    {
                        i++;
                        hiAmount = int.Parse(hiMatch.Groups["amt"].Value) * 256;
                    }
                }
            }

            foreach (var required in _reserveFollowing)
            {
                while (CanContinue() && !_asmLine.IsMatch(_lines[i]))
                    i++;
                if (!CanContinue() || !required.IsMatch(_lines[i]))
                    return false;
                i++;
            }

            int loAmount = int.Parse(m.Groups["amt"].Value);
            if (isRelease)
            {
                if (loAmount != _curFunction.ManuallyReserved)
                    Console.WriteLine($"!! Function {_curFunction.Name} released {loAmount}, but only reserved {_curFunction.ManuallyReserved} !!");
                loAmount *= -1;
            }

            var amt = hiAmount + loAmount;

            if (canRelease == (amt < 1024))
                return false;

            if (canRelease)
            {
                int releaseAmt = UInt16.MaxValue - amt;
                _curFunction.StackUsed -= releaseAmt;
                if (releaseAmt != _curFunction.ManuallyReserved)
                    Console.WriteLine($"!! Function {_curFunction.Name} released {releaseAmt}, but only reserved {_curFunction.ManuallyReserved} !!");
            }
            else
            {
                _curFunction.StackUsed += amt;
                Debug.Assert(_curFunction.ManuallyReserved == 0);
                _curFunction.ManuallyReserved = amt;
            }

            return true;
        }

        bool TryOutsideJump()
        {
            var m = _outsideJump.Match(Line);
            if (!m.Success)
                return false;
            var destName = m.Groups["destName"].Value;
            if (destName == _curFunction.Name || destName == "__prologue_saves__" || destName == "__epilogue_restores__")
                return false;
            //Debug.Assert(_curFunction.Name=="__vectors" || !m.Groups["offset"].Success);
            if (m.Groups["offset"].Success)
            {
                Console.WriteLine($"{_curIdx + 1} : Outside jump with offset: {_curFunction.Name} >> {destName}");
            }
            _curFunction.CallAddresses.Add(int.Parse(m.Groups["dest"].Value, NumberStyles.HexNumber));
            return true;
        }

        bool TryPop()
        {
            var m = _popRegex.Match(Line);
            if (!m.Success)
                return false;
            _curFunction.StackUsed--;
            return true;
        }

        bool TryEpilogue()
        {
            var m = _epilogueRestores.Match(Line);
            if (!m.Success)
                return false;
            _curFunction.StackUsed -= _curFunction.ManuallyReserved;
            return true;
        }
    }
}
