# File Transfer Quickstart

A server which publishes a directory of the host as an OPC UA file system, and a Windows Forms
client which browses it and moves files in both directions.

The file transfer model is **OPC UA Part 5 Annex C** (`FileType`) and **Part 20 §4**
(`FileDirectoryType`): a directory is an object with `CreateDirectory`, `CreateFile`,
`Delete` and `MoveOrCopy` methods, a file is an object with `Open`, `Read`, `Write`,
`SetPosition`, `GetPosition` and `Close`, plus properties such as `Size` and `Writable`.
Neither side of this sample implements any of that. Both use what the SDK ships.

## The server

`FileTransferServer` registers one node manager and writes none:

```csharp
AddNodeManager(new FileSystemNodeManagerFactory(
    new PhysicalFileSystemProvider(rootDirectory, mountName, writable)));
```

`FileSystemNodeManager` (`Opc.Ua.Server.FileSystem`) turns any `IFileSystemProvider` into the
`FileType` / `FileDirectoryType` address space and mounts it below the standard
`Server/FileSystem` object (`i=16314`), which is where a client looks for it.
`PhysicalFileSystemProvider` is the provider the SDK ships for a directory on disk; it rejects
every path which would leave the configured root, so nothing above it is reachable.

A server for a database, a blob store or an in-memory tree differs from this sample in one
line: the provider it constructs. `IFileSystemProvider` is a small async interface -
`GetEntryAsync`, `EnumerateAsync`, `OpenReadAsync`, `OpenWriteAsync`, `CreateDirectoryAsync`,
`CreateFileAsync`, `DeleteAsync`, `MoveAsync`, `CopyAsync`.

Because the directory to publish only becomes known once the configuration has been read, the
factory is registered in `CreateMasterNodeManagerAsync` rather than in the constructor.

### Configuration

`Quickstarts.FileTransferServer.Config.xml` carries the settings of the sample as an extension:

```xml
<FileTransferServerConfiguration xmlns="http://opcfoundation.org/Quickstarts/FileTransfer">
  <RootDirectory>.\FileTransfer</RootDirectory>
  <MountName>SampleFiles</MountName>
  <Writable>true</Writable>
</FileTransferServerConfiguration>
```

A relative `RootDirectory` is resolved against the working directory of the executable and
created if it is missing. The first time the server runs it puts a `ReadMe.txt`, a
`Reports/Trend.csv` and an empty `Uploads` directory there, so that a client has something to
look at; anything a user puts there afterwards is left alone. `Writable` set to `false`
publishes the directory read only, and every write is then refused with
`BadUserAccessDenied` without the server touching the disk.

The server listens on `opc.tcp://localhost:62569/Quickstarts/FileTransferServer`.

## The client

`MainForm` uses `FileSystemClient` (`Opc.Ua.Client.FileSystem`), the `System.IO` shaped
wrapper around the same model:

```csharp
var fileSystem = FileSystemClient.OpenServerFileSystem(session);

await foreach (UaFileSystemInfo entry in fileSystem.EnumerateAsync("/"))
{
    Console.WriteLine($"{(entry.IsDirectory ? "DIR " : "FILE")} {entry.FullPath}");
}
```

The tree on the left shows the directories, the list on the right the content of the selected
one, and the buttons below upload, download, delete and create. A transfer is streamed through
`UaFileStream` in chunks, so a file never has to fit into memory on either side, and the
progress bar follows it.

Two things are worth copying from this client:

- **Only the asynchronous stream members are used.** The synchronous ones on `UaFileStream`
  block on the asynchronous ones, which on the thread of a Windows Forms application is a
  deadlock waiting to happen.
- **Paths are never spelled out.** A server puts its file system in a namespace of its own, so
  the path of an entry carries a namespace prefix whose index only that server knows -
  `/2:SampleFiles/2:Reports` rather than `/SampleFiles/Reports`. The client browses for what it
  wants and keeps the `FullPath` the server handed it. The same holds after a create or a
  move: the server names the new node, so its answer is what the next call uses.

Because the client only relies on the standard `Server/FileSystem` object, it works against any
server which offers one - not only against the sample server here.

## Running it

Start `Quickstarts.FileTransferServer.exe`, then `Quickstarts.FileTransferClient.exe`, and
connect. The client opens on `SampleFiles`.

File handles belong to the session which opened them. When the client reconnects, the file
system client is built again from scratch rather than carried over, because the handles of the
old session are gone.

## See also

- [FileSystemClient.md](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/FileSystemClient.md) - the client side surface in full
- OPC UA Part 5 Annex C - `FileType`, `TemporaryFileTransferType`
- OPC UA Part 20 §4 - the file system object model
