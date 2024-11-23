@echo on
setlocal

echo Building ModelDesign2
Opc.Ua.ModelCompiler.exe compile -version v105  -d2 ".\Instances\ModelDesign2.xml" -d2 "..\Common\Types\ModelDesign1.xml" -cg ".\Instances\ModelDesign2.csv" -o2 .\Instances
echo Success!


