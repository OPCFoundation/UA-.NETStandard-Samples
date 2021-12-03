@echo off
setlocal

echo Building ModelDesign
Opc.Ua.ModelCompiler.exe compile -version v104 -d2 ".\ModelDesign.xml" -cg ".\ModelDesign.csv" -o2 .\
echo Success!

copy *.Classes.cs ..\Client
copy *.Constants.cs ..\Client
copy *.DataTypes.cs ..\Client


