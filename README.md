# avr-stack-analyser
A basic utility to analyse the stack of programs created with avr-gcc.
Create an annotated disassembly of your program using avr-objdump -S, then use this utility to determine stack memory usage or analyse a stack dump.

## Basic usage:
  ```
  StackAnalyser <source file name>
  ```

Complete options:
  ```
  StackAnalyser --source <source> --out <out> --detailed <detailed>
  ```
 OR
  ```
  StackAnalyser --dir <dir of source> --out <out> --detailed <detailed>
  ```

If `--dir` is specified, will analyse the most recent file in the directory.
If no output is specified, output filename will be `<source>_maxStacks.txt`

Basic output contains the stack with the largest memory usage from every root.
Detailed output contains every possible stack the program can generate.

## Stack dump analysis:

Once the utility has run, it will wait for a stack trace on the console. Pressing enter without a stack dump will exit the program.
Stack trace is expected to be hex encoded bytes in this following format:
 - Byte 0: Stack Pointer Low
 - Byte 1: Stack Pointer High
 - Following bytes: Stack dump

Example: for a stack pointer of 2110 (0x083E), the an example input is:
```
3E0806334E30063D00008000084C074F07320731085C086C03017F011400158BC0240A330C061F00EF51191027000022027102F7FF0605640325330C088C000132040002A900089C04524357023D27966000FF584B35BE5054696D656F7574C5C5C5C5C5C5C5C500C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C5C57000FF000000C54A6F525FC5C5C5C5C5C5743257A7A6165A90A9020057017C
```

Which generates an output:
```
SP: 083E (2110)

_ZN8TimerTwo6millisEv : 0x0C66
_ZN8TimerTwo6microsEv : 0x0C7A
delay : 0x0E9E
_Z13InitMessagingv : 0x2B16
main : 0x4F2C
__do_global_ctors : 0x02F8

All stack accounted for (193)
```

The utility will try to figure out the stack trace at the point it was taken.

## Limitations
This is fairly simple right now. It understands the following operations as changing the stack:
 - Push / Pop
 - Prologue Saves / Epilogue Restores
 - rcall .+0
 - Manual adjustment of SP using registers r28 and r29, and the pattern that avr-gcc seems to generate.

It looks for patterns that GCC creates. As such, it is fragile to changes in the GCC implementation or different options

It assumes that functions cannot be called or jumped into partway through.

When calculating maximum stack size, it assumes that any function calling another will add two bytes to the stack (even if via (r)jmp, which is not true).
