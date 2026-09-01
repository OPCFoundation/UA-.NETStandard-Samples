@echo off
setlocal

echo Building ModelDesign1
Opc.Ua.ModelCompiler.exe compile -version v105  -d2 ".\Types\ModelDesign1.xml" -cg ".\Types\ModelDesign1.csv" -o2 .\Types

rem The library compiles the type model with the OPC UA source generator directly
rem from ModelDesign1.xml, so the ModelCompiler C# outputs it replaces must not stay
rem in the Types folder - the duplicated types would break the build. Everything else
rem the ModelCompiler wrote is kept: the node set, the schemas and the .uanodes are
rem still embedded in the assembly and loaded by the server.
del .\Types\*.cs

echo Success!





