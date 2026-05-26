Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
shell.CurrentDirectory = fso.GetParentFolderName(WScript.ScriptFullName)
appExe = shell.CurrentDirectory & "\app\ImagePromptStudio.exe"
If fso.FileExists(appExe) Then
    shell.Run """" & appExe & """", 1, False
Else
    dotnetPath = shell.ExpandEnvironmentStrings("%LOCALAPPDATA%") & "\Microsoft\dotnet\dotnet.exe"
    shell.Run """" & dotnetPath & """ run --project """ & shell.CurrentDirectory & "\ImagePromptStudio.csproj""", 1, False
End If
