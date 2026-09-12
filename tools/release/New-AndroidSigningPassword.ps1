# Run only on Windows, locally. Passwords never pass through command-line arguments.
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PasswordFile)
$ErrorActionPreference = 'Stop'
if ($env:CI -or $env:GITHUB_ACTIONS) { throw 'Owner computer only.' }
$destination = [IO.Path]::GetFullPath($PasswordFile)
if (Test-Path -LiteralPath $destination) { throw 'An encrypted password already exists; reuse it.' }
$directory = [IO.Path]::GetDirectoryName($destination)
$ancestor = $directory
while ($ancestor) {
    if (Test-Path (Join-Path $ancestor '.git')) { throw 'Password storage must be outside Git.' }
    $parent = [IO.Directory]::GetParent($ancestor)
    $ancestor = if ($parent) { $parent.FullName } else { $null }
}
New-Item -ItemType Directory -Path $directory -Force | Out-Null
# A dedicated private directory, with only the owner and SYSTEM able to read it.
$owner = [Security.Principal.WindowsIdentity]::GetCurrent().User
# icacls updates the DACL without requesting the unrelated SeSecurityPrivilege.
& icacls.exe $directory /inheritance:r /grant:r "*$($owner.Value):(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot restrict password directory permissions.' }
$acl = Get-Acl -LiteralPath $directory
foreach ($rule in $acl.Access) {
    $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
    if ($rule.AccessControlType -eq 'Allow' -and $sid -notin @($owner.Value, 'S-1-5-18')) { throw 'Password directory grants access to an unexpected principal.' }
}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$form = New-Object Windows.Forms.Form
$form.Text = 'MementoMori：设置长期签名密码'
$form.Size = New-Object Drawing.Size(540, 285)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.TopMost = $true
$label = New-Object Windows.Forms.Label
$label.Text = "请设置至少 12 个字符的签名密码，并在下框再次输入。`n这不是游戏密码。请把它保存在自己的密码管理器中。`n本机只保存 Windows 加密后的密码，不会发送到聊天。"
$label.SetBounds(20, 15, 490, 65)
$first = New-Object Windows.Forms.TextBox
$first.UseSystemPasswordChar = $true
$first.SetBounds(20, 90, 480, 28)
$second = New-Object Windows.Forms.TextBox
$second.UseSystemPasswordChar = $true
$second.SetBounds(20, 130, 480, 28)
$save = New-Object Windows.Forms.Button
$save.Text = '保存密码并继续'
$save.SetBounds(320, 180, 180, 35)
$save.Add_Click({
    if ($first.Text.Length -lt 12 -or $first.Text -cne $second.Text) {
        [void][Windows.Forms.MessageBox]::Show('请输入至少 12 个字符，且两次输入相同。', '请检查密码')
        return
    }
    $secure = ConvertTo-SecureString $first.Text -AsPlainText -Force
    try {
        $encrypted = ConvertFrom-SecureString $secure
        # CreateNew rejects a concurrent overwrite of a saved credential.
        $stream = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
        try {
            $bytes = [Text.Encoding]::ASCII.GetBytes($encrypted)
            $stream.Write($bytes, 0, $bytes.Length)
        } finally { $stream.Dispose() }
        $first.Clear(); $second.Clear()
        $form.DialogResult = [Windows.Forms.DialogResult]::OK
        $form.Close()
    } finally { $secure.Dispose() }
})
$form.Controls.AddRange(@($label, $first, $second, $save))
$form.AcceptButton = $save
try {
    if ($form.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) { throw 'Password setup canceled; no signing identity was created.' }
    Write-Host 'Local Windows-encrypted signing password saved.'
} finally { $first.Clear(); $second.Clear(); $form.Dispose() }
