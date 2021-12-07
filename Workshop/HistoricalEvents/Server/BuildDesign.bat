@echo off
setlocal

echo Building ModelDesign
Opc.Ua.ModelCompiler.exe compile -version v104 -d2 ".\Model\ModelDesign.xml" -cg ".\Model\ModelDesign.csv" -o2 .\Model\
echo Success!

copy Model\*.Classes.cs ..\Client
copy Model\*.Constants.cs ..\Client
copy Model\*.DataTypes.cs ..\Client


