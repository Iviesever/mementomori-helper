Option Explicit

Dim shell, fso, root, launcher, command
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

root = fso.GetParentFolderName(WScript.ScriptFullName)
launcher = fso.BuildPath(root, "tools\safe-export\Start-MementoMori-Exporter-Silent.ps1")

If Not fso.FileExists(launcher) Then
    MsgBox "Exporter launcher script was not found:" & vbCrLf & launcher, vbCritical, "MementoMori Account Exporter"
    WScript.Quit 1
End If

shell.Popup "Starting MementoMori Account Exporter...", 2, "MementoMori Account Exporter", 64

command = "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File " & Chr(34) & launcher & Chr(34)
shell.Run command, 0, False
