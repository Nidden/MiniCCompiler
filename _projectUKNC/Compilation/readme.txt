RT-11 emulator for Windows console ( 16.01.2022 )

The main purpose - to run the MACRO-11 compiler in Windows. Also possible to run RT-11 programs and to apply RT-11 commands and utilities to the files in the current Windows directory.


Install
-------
1. Place rt11.exe in Windows directory or add [RT-11 Emulator] directory to Windows PATH.

2. Run rt11.exe, right-click on title bar, select "Properties" and set Font, Layout and Colors.

3. Type command:
copy/dev/file/noq sy: SYSTEM.DSK
to extract system image for more flexible operation ( not necessary ).

4. Press <F12> to end emulation.


Use
---
1. Associate SAV and BAS file extensions with rt11.exe to automatically start such files in emulator.

2. Use double quoted path to SAV or BAS file as rt11.exe command line argument to start such files in emulator ( rt11 "D:\rt11\Demo\KOI8.BAS" ).

3. Run rt11.exe to load RT-11 with logical device name DK: assigned to current Windows directory. Type EXIT or press <F12> to exit emulation.

4. Run rt11.exe with RT-11 command as command line argument to apply this command to content of current Windows directory ( rt11 dir/fu >Files.txt ).

5. Use RT-11 HELP command to get help ( rt11 help copy >Copy.txt ).

6. Besides standard RT-11 utilities system image includes some additional programs:
6.1. rt11 dhry		- to run emulation speed test
6.2. rt11 tetris	- to run original TETRIS game
6.3. rt11 xonix         - to run XONIX game
6.4. rt11 klop          - to run KLOP game (left arrow to start)
