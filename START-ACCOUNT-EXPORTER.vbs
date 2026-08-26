Option Explicit

Dim shell, fso, root, launcher
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

root = fso.GetParentFolderName(WScript.ScriptFullName)
launcher = fso.BuildPath(root, "tools\safe-export\Start-MementoMori-Exporter-Silent.ps1")

If Not fso.FileExists(launcher) Then
    MsgBox "找不到账号导出器启动脚本：" & vbCrLf & launcher, vbCritical, "MementoMori 账号导出器"
    WScript.Quit 1
End If

shell.Run "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & launcher & """", 0, False
