@echo off
setlocal

echo Building ModelDesign
Opc.Ua.ModelCompiler.exe compile -version v104 -d2 ".\Model\ModelDesign.xml" -cg ".\Model\ModelDesign.csv" -o2 ".\Model"
echo Success!

copy .\Model\*.Constants.cs ..\Client
copy .\Model\*.DataTypes.cs ..\Client

rem The server compiles the model with the OPC UA source generator directly from
rem ModelDesign.xml, so the ModelCompiler outputs it replaces must not stay in
rem the Model folder - the duplicated types would break the build.
del .\Model\*.cs
del .\Model\*.uanodes
