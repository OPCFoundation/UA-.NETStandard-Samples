@echo off
setlocal

echo Building ModelDesign2
Opc.Ua.ModelCompiler.exe compile -version v104  -d2 ".\Instances\ModelDesign2.xml" -cg ".\Instances\ModelDesign2.csv" -o2 .\Instances
echo Success!


