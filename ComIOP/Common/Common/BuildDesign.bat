@echo off
setlocal

Opc.Ua.ModelCompiler.exe -version v104 -d2 ".\ModelDesign.xml" -c ".\ModelDesign.csv" -o ".\" 