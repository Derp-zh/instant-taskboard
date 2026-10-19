param([Parameter(Mandatory)][string]$DataDirectory)
$ErrorActionPreference='Stop'
$taskRepository=Split-Path -Parent $PSScriptRoot
$taskData=[IO.Path]::GetFullPath($DataDirectory)
if(-not $taskData.StartsWith((Join-Path $taskRepository 'test-output')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw '仅支持隔离测试目录'}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class HotkeyImeSink {
 [DllImport("imm32.dll")] public static extern IntPtr ImmGetContext(IntPtr window);
 [DllImport("imm32.dll")] public static extern bool ImmReleaseContext(IntPtr window,IntPtr context);
 [DllImport("imm32.dll")] public static extern bool ImmSetOpenStatus(IntPtr context,bool open);
 [DllImport("imm32.dll")] public static extern bool ImmGetOpenStatus(IntPtr context);
 [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint thread);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window,int command);
}
'@
$taskOriginalLanguage=[Windows.Forms.InputLanguage]::CurrentInputLanguage
$taskForm=[Windows.Forms.Form]::new(); $taskForm.Text='快捷键中英文隔离验证'; $taskForm.ShowInTaskbar=$false; $taskForm.TopMost=$true
$taskForm.StartPosition='Manual'; $taskArea=[Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$taskForm.SetBounds($taskArea.Right-360,$taskArea.Bottom-180,340,150)
$taskInput=[Windows.Forms.TextBox]::new(); $taskInput.Multiline=$true; $taskInput.Dock='Fill'; $taskForm.Controls.Add($taskInput)
$taskTimer=[Windows.Forms.Timer]::new(); $taskTimer.Interval=50
$taskStarted=[DateTime]::UtcNow; $taskSerial=-1; $taskOriginalOpen=$false; $taskOriginalKnown=$false
$taskTimer.Add_Tick({
 try{
  if(([DateTime]::UtcNow-$taskStarted).TotalSeconds -gt 120){$taskForm.Close(); return}
  $taskCommandPath=Join-Path $taskData 'sink-command.json'
  if(-not (Test-Path -LiteralPath $taskCommandPath)){return}
  $taskCommand=Get-Content -LiteralPath $taskCommandPath -Raw | ConvertFrom-Json
  if($taskCommand.Serial -eq $script:taskSerial){return}; $script:taskSerial=$taskCommand.Serial
  if($taskCommand.Mode -eq 'close'){$taskForm.Close(); return}
  $taskChinese=@([Windows.Forms.InputLanguage]::InstalledInputLanguages | Where-Object {$_.Culture.Name -like 'zh*'})
  $taskEnglish=@([Windows.Forms.InputLanguage]::InstalledInputLanguages | Where-Object {$_.Culture.Name -like 'en*'})
  $taskLanguage=if($taskCommand.Mode -eq 'chinese' -or $taskEnglish.Count -eq 0){$taskChinese | Select-Object -First 1}else{$taskEnglish[0]}
  if($null -ne $taskLanguage){[Windows.Forms.InputLanguage]::CurrentInputLanguage=$taskLanguage}
  [HotkeyImeSink]::ShowWindow($taskForm.Handle,5) | Out-Null
  $taskForm.Activate(); $taskInput.Focus() | Out-Null
  $taskInput.ImeMode=if($taskCommand.Mode -eq 'chinese'){[Windows.Forms.ImeMode]::On}else{[Windows.Forms.ImeMode]::Off}
  $taskContext=[HotkeyImeSink]::ImmGetContext($taskInput.Handle)
  $taskOpen=$false
  if($taskContext -ne [IntPtr]::Zero){[HotkeyImeSink]::ImmSetOpenStatus($taskContext,($taskCommand.Mode -eq 'chinese')) | Out-Null; $taskOpen=[HotkeyImeSink]::ImmGetOpenStatus($taskContext); [HotkeyImeSink]::ImmReleaseContext($taskInput.Handle,$taskContext) | Out-Null}
  $taskState=@{Serial=$taskSerial;Mode=$taskCommand.Mode;Window=$taskForm.Handle.ToInt64();TextWindow=$taskInput.Handle.ToInt64();Culture=[Windows.Forms.InputLanguage]::CurrentInputLanguage.Culture.Name;Layout=[HotkeyImeSink]::GetKeyboardLayout(0).ToInt64();ImeOpen=$taskOpen;HasChinese=($taskChinese.Count -gt 0);ForegroundOwn=([HotkeyImeSink]::GetForegroundWindow() -eq $taskForm.Handle)}
  [IO.File]::WriteAllText((Join-Path $taskData 'sink-state.tmp'),($taskState | ConvertTo-Json)); Move-Item -LiteralPath (Join-Path $taskData 'sink-state.tmp') -Destination (Join-Path $taskData 'sink-state.json') -Force
 }catch{[IO.File]::WriteAllText((Join-Path $taskData 'sink-error.txt'),$_.ToString());$taskForm.Close()}
})
$taskForm.Add_Shown({
 $taskInput.Focus() | Out-Null
 $taskContext=[HotkeyImeSink]::ImmGetContext($taskInput.Handle)
 if($taskContext -ne [IntPtr]::Zero){$script:taskOriginalOpen=[HotkeyImeSink]::ImmGetOpenStatus($taskContext);$script:taskOriginalKnown=$true;[HotkeyImeSink]::ImmReleaseContext($taskInput.Handle,$taskContext) | Out-Null}
 $taskTimer.Start()
})
try{[Windows.Forms.Application]::Run($taskForm)}finally{
 $taskTimer.Stop();$taskTimer.Dispose();[Windows.Forms.InputLanguage]::CurrentInputLanguage=$taskOriginalLanguage
 $taskContext=[HotkeyImeSink]::ImmGetContext($taskInput.Handle)
 if($taskContext -ne [IntPtr]::Zero){if($taskOriginalKnown){[HotkeyImeSink]::ImmSetOpenStatus($taskContext,$taskOriginalOpen) | Out-Null};[HotkeyImeSink]::ImmReleaseContext($taskInput.Handle,$taskContext) | Out-Null}
 $taskForm.Dispose()
}
