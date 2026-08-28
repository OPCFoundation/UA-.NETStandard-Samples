@echo off
setlocal

echo Building ModelDesign
Opc.Ua.ModelCompiler.exe compile -version v104 -d2 ".\ModelDesign.xml" -cg ".\ModelDesign.csv" -o2 .\
echo Success!

rem The client still compiles the code the model compiler generates. The server
rem generates its own copy from ModelDesign.xml at build time through the
rem OPCFoundation.NetStandard.Opc.Ua.SourceGeneration package, so the
rem intermediate files would clash with the source generator and are removed
rem again, together with the binary node set only the old server consumed.
copy *.Classes.cs ..\Client
copy *.Constants.cs ..\Client
copy *.DataTypes.cs ..\Client
del *.Classes.cs *.Constants.cs *.DataTypes.cs *.PredefinedNodes.uanodes
