@echo off
setlocal

echo Building ModelDesign
Opc.Ua.ModelCompiler.exe compile -version v104 -d2 ".\ModelDesign.xml" -cg ".\ModelDesign.csv" -o2 .\
echo Success!

copy Quickstarts.Boiler.Constants.cs ..\Client
copy Quickstarts.Boiler.DataTypes.cs ..\Client


