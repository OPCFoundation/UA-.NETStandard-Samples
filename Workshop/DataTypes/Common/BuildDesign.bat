@echo off
setlocal

echo Building ModelDesign1
Opc.Ua.ModelCompiler.exe compile -version v104  -d2 ".\Types\ModelDesign1.xml" -cg ".\Types\ModelDesign1.csv" -o2 .\Types
echo Success!





