copy "a.mac" "Compilation\"
cd  Compilation
rt11.exe macro a.mac
rt11.exe link a.obj
copy "a.sav" "../"
del "a.*"
cd ../
rt11dsk d "uknc.dsk" "a.sav"
rt11dsk a "uknc.dsk" "a.sav"
rem del "a.sav"
UKNCBTL.exe