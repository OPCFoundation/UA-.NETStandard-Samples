#!/bin/bash
echo Test the Mono console server and console client
workdir=$(pwd)
testresult=0

cd Samples/NetCoreConsoleServer
echo build server
rm -r obj
msbuild /p:configuration=Debug /p:VisualStudioVersion=15.0 /p:MonoBuild=true /t:restore,clean,build NetCoreConsoleServer.csproj
echo start server
cd bin/Debug/net462
mono NetCoreConsoleServer.exe -t 60 -a &
serverpid="$!"
cd "$workdir"

cd Samples/NetCoreConsoleClient
echo build client
rm -r obj
msbuild /p:configuration=Debug /p:VisualStudioVersion=15.0 /p:MonoBuild=true /t:restore,clean,build NetCoreConsoleClient.csproj
echo start client
cd bin/Debug/net462
mono NetCoreConsoleClient.exe -t 20 -a &
clientpid="$!"
cd "$workdir"

echo wait for client
wait $clientpid
if [ $? -eq 0 ]; then
	echo "SUCCESS - Client test passed"
else
	testresult=$?
	echo "FAILED - Client test failed with a status of $testresult"
fi

echo wait for server
wait $serverpid
serverresult=$?

if [ $? -eq 0 ]; then
	echo "SUCCESS - Server test passed"
else
	serverresult=$?
	echo "FAILED - Server test failed with a status of $serverresult"
fi

exit $testresult



