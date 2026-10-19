param([string]$Executable='release/即时任务看板-0.9.6-local/即时任务看板.exe', [string]$DataDirectory='test-output/task-switcher-096-01')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class SwitcherInput96 {
 public delegate bool WindowProc(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] public static extern bool EnumWindows(WindowProc p,IntPtr data);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint process);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern bool SetWindowText(IntPtr h,string text);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect rect);
 [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 public static string Class(IntPtr h) {var s=new StringBuilder(256);GetClassName(h,s,s.Capacity);return s.ToString();}
 public static void Key(byte key,bool up) {keybd_event(key,0,up?2u:0u,UIntPtr.Zero);}
}
'@
$scriptText=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Floating-Interaction.ps1') -Raw
$scriptText=$scriptText.Replace('$repository = Split-Path -Parent $PSScriptRoot', '$repository = (Get-Location).Path')
$marker="    Double-Press (Wait-Ready 'floating-blank-ready.json') 'blank' 'blank.signal'"
$insertion=@'
    $switcherReady=Wait-Ready 'floating-blank-ready.json'
    $uniquePrefix='TB096-ISOLATED-'
    $targetProcess=$probe.Id
    $rename=[SwitcherInput96+WindowProc]{param($handle,$unused)
        $processNumber=0u; [SwitcherInput96]::GetWindowThreadProcessId($handle,[ref]$processNumber) | Out-Null
        if ($processNumber -eq $targetProcess) { [SwitcherInput96]::SetWindowText($handle,($uniquePrefix+$handle.ToInt64())) | Out-Null }; return $true
    }
    [SwitcherInput96]::EnumWindows($rename,[IntPtr]::Zero) | Out-Null
    try {
        [SwitcherInput96]::Key(0x12,$false); [SwitcherInput96]::Key(0x09,$false); [SwitcherInput96]::Key(0x09,$true)
        Start-Sleep -Milliseconds 700
        $switcher=[SwitcherInput96]::GetForegroundWindow(); $switcherClass=[SwitcherInput96]::Class($switcher)
        if ($switcherClass -notin @('MultitaskingViewFrame','XamlExplorerHostIslandWindow','#32771')) { throw ('没有定位到真实Alt+Tab视图：'+$switcherClass) }
        $element=[Windows.Automation.AutomationElement]::FromHandle($switcher)
        $entries=$element.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
        $matches=@(); $named=0
        foreach ($entry in $entries) { $name=$entry.Current.Name; if($name){$named++}; if($name -and $name.Contains($uniquePrefix)){$matches+=$name} }
        [ordered]@{ShellClass=$switcherClass;NamedElements=$named;Matches=$matches} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $probeData 'task-switcher-diagnostic.json') -Encoding utf8
        if ($matches.Count -ne 0) { throw ('Alt+Tab仍包含隔离窗口：'+$matches.Count) }
        if ($named -lt 2) {
            $rect=[SwitcherInput96+Rect]::new(); [SwitcherInput96]::GetWindowRect($switcher,[ref]$rect) | Out-Null
            $bitmap=[Drawing.Bitmap]::new(($rect.Right-$rect.Left),($rect.Bottom-$rect.Top)); $graphics=[Drawing.Graphics]::FromImage($bitmap)
            try {$graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$bitmap.Size);$bitmap.Save((Join-Path $probeData 'task-switcher.png'),[Drawing.Imaging.ImageFormat]::Png)} finally {$graphics.Dispose();$bitmap.Dispose()}
        }
        [ordered]@{Passed=($named -ge 2);ShellClass=$switcherClass;NamedElements=$named;MatchingIsolatedWindows=$matches.Count;PhysicalAltTab=$true;VisualReviewPending=($named -lt 2);OtherWindowTitlesEnumerated=$false;ScreenshotContainsDesktopContent=($named -lt 2);ScreenshotExcludedFromPublic=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $probeData 'task-switcher-report.json') -Encoding utf8
    } finally {
        [SwitcherInput96]::Key(0x1b,$false); [SwitcherInput96]::Key(0x1b,$true); [SwitcherInput96]::Key(0x12,$true)
    }
    Start-Sleep -Milliseconds 350
    Double-Press $switcherReady 'blank' 'blank.signal'
'@
if (-not $scriptText.Contains($marker)) { throw '交互检查插入点未匹配，停止执行。' }
$scriptText=$scriptText.Replace($marker,$insertion)
& ([scriptblock]::Create($scriptText)) -Executable $Executable -DataDirectory $DataDirectory
Get-Content -LiteralPath (Join-Path (Join-Path (Split-Path -Parent $PSScriptRoot) $DataDirectory) 'task-switcher-report.json') -Raw
