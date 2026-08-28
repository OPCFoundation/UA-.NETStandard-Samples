@echo off
setlocal

echo Building ModelDesign
Opc.Ua.ModelCompiler.exe compile -version v104 -d2 ".\Model\ModelDesign.xml" -cg ".\Model\ModelDesign.csv" -o2 .\Model\
echo Success!

rem The client still compiles the code the model compiler generates. The server
rem generates its own copy from Model\ModelDesign.xml at build time through the
rem OPCFoundation.NetStandard.Opc.Ua.SourceGeneration package, so the
rem intermediate files would clash with the source generator and are removed
rem again, together with the binary node set only the old server consumed.
copy Model\*.Classes.cs ..\Client
copy Model\*.Constants.cs ..\Client
copy Model\*.DataTypes.cs ..\Client
del Model\*.Classes.cs Model\*.Constants.cs Model\*.DataTypes.cs Model\*.PredefinedNodes.uanodes
