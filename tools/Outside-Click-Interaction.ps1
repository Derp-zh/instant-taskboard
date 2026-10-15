param([string]$Executable = 'release/即时任务看板-0.9.4/即时任务看板.exe', [string]$DataDirectory = 'test-output/outside-click-094-01', [switch]$ExpectDoubleClick)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$taskData = [IO.Path]::GetFullPath((Join-Path $repository $DataDirectory))
if (-not $taskData.StartsWith((Join-Path $repository 'test-output') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskData)) { throw '必须使用test-output下的新隔离目录。' }
Add-Type -AssemblyName System.Windows.Forms, UIAutomationClient, UIAutomationTypes, WindowsBase
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
public static class OutsideProbe {
  [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
  public sealed class Window { public IntPtr Handle; public string Title; public Rect Bounds; }
  delegate bool Callback(IntPtr h,IntPtr p);
  [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback,IntPtr p);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h,Callback callback,IntPtr p);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h,out Rect r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
  [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
  [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h,uint flags);
  public static IntPtr RootAt(int x,int y) { return GetAncestor(WindowFromPoint(new Point {X=x,Y=y}),2); }
  public static IntPtr HitAt(int x,int y) { return WindowFromPoint(new Point {X=x,Y=y}); }
  public static uint ProcessAt(int x,int y) { GetWindowThreadProcessId(WindowFromPoint(new Point {X=x,Y=y}),out var id); return id; }
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] static extern bool GetCursorPos(out Point p);
  [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h,IntPtr dc);
  [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc,int x,int y);
  public static Point Cursor() { GetCursorPos(out var p); return p; }
  public static void Move(int x,int y) { SetCursorPos(x,y); }
  public static void Click(int x,int y) { SetCursorPos(x,y); mouse_event(2,0,0,0,UIntPtr.Zero); Thread.Sleep(25); mouse_event(4,0,0,0,UIntPtr.Zero); }
  public static Window[] Windows(int id) { var list=new List<Window>(); EnumWindows((h,p)=>{ GetWindowThreadProcessId(h,out var pid); if(pid==(uint)id && IsWindowVisible(h)) { var text=new StringBuilder(256); GetWindowText(h,text,256); GetWindowRect(h,out var r); list.Add(new Window {Handle=h,Title=text.ToString(),Bounds=r}); } return true; },IntPtr.Zero); return list.ToArray(); }
  public static IntPtr[] Children(IntPtr h) { var list=new List<IntPtr>(); EnumChildWindows(h,(v,p)=>{ list.Add(v); return true; },IntPtr.Zero); return list.ToArray(); }
  public static Window Describe(IntPtr h) { var text=new StringBuilder(256); GetWindowText(h,text,256); GetWindowRect(h,out var r); return new Window { Handle=h,Title=text.ToString()+" visible="+IsWindowVisible(h),Bounds=r }; }
  public sealed class Pixels { public int Samples,MaxDifference; public uint Initial,AtMaximum,Final; }
  public static Pixels Sample(int x,int y,Action click) {
    var ready=new ManualResetEventSlim();
    var worker=Task.Run(()=>{ SetThreadDpiAwarenessContext(new IntPtr(-4)); var result=new Pixels(); var dc=GetDC(IntPtr.Zero); try { var first=GetPixel(dc,x,y); if(first==uint.MaxValue) throw new Exception("无效屏幕采样"); result.Initial=first; ready.Set(); var clock=Stopwatch.StartNew(); while(clock.ElapsedMilliseconds<450) { var p=GetPixel(dc,x,y); var diff=Math.Abs((int)(p&255)-(int)(first&255))+Math.Abs((int)((p>>8)&255)-(int)((first>>8)&255))+Math.Abs((int)((p>>16)&255)-(int)((first>>16)&255)); if(diff>result.MaxDifference){result.MaxDifference=diff;result.AtMaximum=p;} result.Final=p; result.Samples++; Thread.Sleep(2); } return result; } finally { ReleaseDC(IntPtr.Zero,dc); } });
    if(!ready.Wait(3000)) throw new Exception("屏幕采样准备超时"); click(); return worker.GetAwaiter().GetResult();
  }
}
'@
function Window($name) {
    for ($attempt=0; $attempt -lt 100; $attempt++) {
        $result = @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -eq $name)
        if ($result.Count) { return $result[0] }; Start-Sleep -Milliseconds 50
    }
    [IO.Directory]::CreateDirectory($taskData) | Out-Null
    @([OutsideProbe]::Windows($taskProcess.Id) | ForEach-Object { [OutsideProbe]::Describe($_.Handle); foreach ($child in [OutsideProbe]::Children($_.Handle)) { [OutsideProbe]::Describe($child) } }) | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskData 'window-search-failure.json') -Encoding utf8
    throw ('没有找到隔离窗口：' + $name)
}
function Control($window, $type, $name) {
    foreach ($handle in @([OutsideProbe]::Children($window.Handle))) {
        $root = [Windows.Automation.AutomationElement]::FromHandle($handle)
        $condition = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,$type)
        foreach ($element in $root.FindAll([Windows.Automation.TreeScope]::Descendants,$condition)) { if (-not $name -or $element.Current.Name -eq $name) { return $element } }
    }; throw ('没有找到控件：' + $name)
}
function Invoke($element) { ([Windows.Automation.InvokePattern]$element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke() }
function BoardPoint($floating) {
    foreach ($w in @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -in @('灵感库','完善中','进行中','待操作','暂搁置','已完成'))) {
        $a = [int]($w.Bounds.Left+25); $b = [int]($w.Bounds.Right-25); $y = [int]($w.Bounds.Top+18)
        if ([OutsideProbe]::RootAt($a,$y) -eq $w.Handle -and [OutsideProbe]::RootAt($b,$y) -eq $w.Handle) { return @{ X=$a; OtherX=$b; Y=$y } }
    }; throw '找不到悬浮窗口之外的本看板区域。'
}
function ToggleOrb {
    $b = Window '即时任务看板入口'
    [OutsideProbe]::SetWindowPos($b.Handle,[IntPtr]::new(-1),$b.Bounds.Left,$b.Bounds.Top,0,0,0x11) | Out-Null
    [OutsideProbe]::Click(($b.Bounds.Right-($b.Bounds.Bottom-$b.Bounds.Top)/2),($b.Bounds.Top+$b.Bounds.Bottom)/2)
    Start-Sleep -Milliseconds 700
}
[OutsideProbe]::SetThreadDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null
$before = [OutsideProbe]::GetForegroundWindow(); $cursorBefore = [OutsideProbe]::Cursor(); $taskProcess = $null; $sink = $null; $sinkProcess = $null
try {
    $taskProcess = Start-Process -FilePath (Join-Path $repository $Executable) -ArgumentList @('--data-dir',$taskData,'--no-integrations') -WindowStyle Hidden -PassThru
    $ball = Window '即时任务看板入口'; Start-Sleep -Milliseconds 700
    # Keep the isolated orb away from the user's existing orb, so the physical
    # click cannot be delivered to another version with the same title/position.
    [OutsideProbe]::SetWindowPos($ball.Handle,[IntPtr]::new(-1),350,180,0,0,0x11) | Out-Null
    Start-Sleep -Milliseconds 250; $ball = Window '即时任务看板入口'
    [OutsideProbe]::SetForegroundWindow($ball.Handle) | Out-Null; Start-Sleep -Milliseconds 500
    if ([OutsideProbe]::RootAt(($ball.Bounds.Left+$ball.Bounds.Right)/2,($ball.Bounds.Top+$ball.Bounds.Bottom)/2) -ne $ball.Handle) { throw '测试悬浮球被其他窗口覆盖，不能点击。' }
    [OutsideProbe]::Click(($ball.Bounds.Left+$ball.Bounds.Right)/2,($ball.Bounds.Top+$ball.Bounds.Bottom)/2)
    $at = [OutsideProbe]::Cursor()
    @{ Cursor=$at; Hit=[OutsideProbe]::Describe([OutsideProbe]::HitAt($at.X,$at.Y)); Foreground=[OutsideProbe]::Describe([OutsideProbe]::GetForegroundWindow()) } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskData 'initial-hit.json') -Encoding utf8
    $shot = [Drawing.Bitmap]::new($ball.Bounds.Right-$ball.Bounds.Left,$ball.Bounds.Bottom-$ball.Bounds.Top); $paint=[Drawing.Graphics]::FromImage($shot)
    try { $paint.CopyFromScreen($ball.Bounds.Left,$ball.Bounds.Top,0,0,$shot.Size); $shot.Save((Join-Path $taskData 'initial-ball.png')) } finally { $paint.Dispose(); $shot.Dispose() }
    @([OutsideProbe]::Windows($taskProcess.Id) | ForEach-Object { [OutsideProbe]::Describe($_.Handle); foreach ($child in [OutsideProbe]::Children($_.Handle)) { [OutsideProbe]::Describe($child) } }) | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskData 'initial-click-windows.json') -Encoding utf8
    Start-Sleep -Milliseconds 350
    $lane = Window '待操作'; Invoke (Control $lane ([Windows.Automation.ControlType]::Button) '＋ 新建任务')
    $draft = Window '新建任务'; Start-Sleep -Milliseconds 500
    $input = Control $draft ([Windows.Automation.ControlType]::Edit) ''
    ([Windows.Automation.ValuePattern]$input.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).SetValue('隔离外点验证草稿')
    $box = $input.Current.BoundingRectangle; [OutsideProbe]::Click($box.Left+15,$box.Top+$box.Height/2)
    Start-Sleep -Milliseconds 900
    $sinkProcess = Start-Process -FilePath ([Environment]::ProcessPath) -ArgumentList @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Outside-Click-Sink.ps1'), '-DataDirectory', $taskData) -WindowStyle Hidden -PassThru
    for ($i=0; $i -lt 100 -and -not (Test-Path -LiteralPath (Join-Path $taskData 'sink-ready.txt')); $i++) { Start-Sleep -Milliseconds 50 }
    $sink = @{ Left=50; Top=50; Handle=[IntPtr]::new([long](Get-Content -LiteralPath (Join-Path $taskData 'sink-ready.txt') -Raw)) }
    [OutsideProbe]::SetForegroundWindow($draft.Handle) | Out-Null; Start-Sleep -Milliseconds 150
    $scale = ($draft.Bounds.Bottom-$draft.Bounds.Top)/650
    $boardPoint = BoardPoint $draft
    $clickAction = [Action]{ [OutsideProbe]::Click($boardPoint.X,$boardPoint.Y) }
    $pixels = [OutsideProbe]::Sample(($draft.Bounds.Left+12*$scale),($draft.Bounds.Top+100*$scale),$clickAction)
    $firstVisible = @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -eq '新建任务').Count -eq 1
    $slowVisible = $null; $differentVisible = $null; $doubleClosed = $null
    $sharedHost = $null; $newBackgroundDifference = $null; $settingsSameHost = $null; $settingsBackgroundDifference = $null; $settingsSingleVisible = $null
    $settingsSingleDifference = $null; $settingsDoubleClosed = $null; $draftCloseKeepsLists = $null; $settingsCloseKeepsLists = $null; $freshOutsideCollapses = $null
    $draftExternalCollapsed = $null; $draftGroupRestored = $null; $draftRestoredValue = $null; $settingsExternalCollapsed = $null; $settingsGroupRestored = $null
    if ($ExpectDoubleClick) {
        $newAction = [Action]{ Invoke (Control $lane ([Windows.Automation.ControlType]::Button) '＋ 新建任务') }
        $newPixels = [OutsideProbe]::Sample(($draft.Bounds.Left+12*$scale),($draft.Bounds.Top+100*$scale),$newAction)
        $newDraft = Window '新建任务'; $sharedHost = $newDraft.Handle -eq $draft.Handle; $newBackgroundDifference = $newPixels.MaxDifference
        $newInput = Control $newDraft ([Windows.Automation.ControlType]::Edit) ''
        ([Windows.Automation.ValuePattern]$newInput.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).SetValue('整组收起仍保留最后输入')
        Start-Sleep -Milliseconds 200
        [OutsideProbe]::Click($sink.Left+60,$sink.Top+70); Start-Sleep -Milliseconds 500
        $draftExternalCollapsed = @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -in @('新建任务','待操作')).Count -eq 0
        ToggleOrb
        $restoredDraft = Window '新建任务'; $draftGroupRestored = $restoredDraft.Handle -eq $draft.Handle -and @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -eq '待操作').Count -eq 1
        $restoredInput = Control $restoredDraft ([Windows.Automation.ControlType]::Edit) ''
        $draftRestoredValue = ([Windows.Automation.ValuePattern]$restoredInput.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)).Current.Value -eq '整组收起仍保留最后输入'
        $boardPoint = BoardPoint $restoredDraft
        Start-Sleep -Milliseconds ([OutsideProbe]::GetDoubleClickTime()+100)
        [OutsideProbe]::Click($boardPoint.X,$boardPoint.Y); Start-Sleep -Milliseconds 150
        $slowVisible = @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -eq '新建任务').Count -eq 1
        [OutsideProbe]::Click($boardPoint.OtherX,$boardPoint.Y); Start-Sleep -Milliseconds 100
        $differentVisible = @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -eq '新建任务').Count -eq 1
        Start-Sleep -Milliseconds ([OutsideProbe]::GetDoubleClickTime()+100)
        [OutsideProbe]::Click($boardPoint.X,$boardPoint.Y); Start-Sleep -Milliseconds 70; [OutsideProbe]::Click($boardPoint.X,$boardPoint.Y); Start-Sleep -Milliseconds 400
        $doubleClosed = @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -eq '新建任务').Count -eq 0
        Start-Sleep -Milliseconds 750
        $draftCloseKeepsLists = @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -eq '待操作').Count -eq 1
        $ball = Window '即时任务看板入口'; $settingsButton = Control $ball ([Windows.Automation.ControlType]::Button) '设置'
        Invoke $settingsButton; Invoke $settingsButton; Start-Sleep -Milliseconds 700
        $settingsWindow = Window '即时任务看板设置'
        $boardPoint = BoardPoint $settingsWindow
        $settingsAction = [Action]{ Invoke $settingsButton }
        $settingsPixels = [OutsideProbe]::Sample(($settingsWindow.Bounds.Left+16*$scale),($settingsWindow.Bounds.Top+100*$scale),$settingsAction)
        $settingsAgain = Window '即时任务看板设置'; $settingsSameHost = $settingsAgain.Handle -eq $settingsWindow.Handle; $settingsBackgroundDifference = $settingsPixels.MaxDifference
        $settingsSinglePixels = [OutsideProbe]::Sample(($settingsWindow.Bounds.Left+16*$scale),($settingsWindow.Bounds.Top+100*$scale),$clickAction)
        $settingsSingleVisible = @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -eq '即时任务看板设置').Count -eq 1
        $settingsSingleDifference = $settingsSinglePixels.MaxDifference
        [OutsideProbe]::Click($sink.Left+60,$sink.Top+70); Start-Sleep -Milliseconds 500
        $settingsExternalCollapsed = @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -in @('即时任务看板设置','待操作')).Count -eq 0
        ToggleOrb
        $restoredSettings = Window '即时任务看板设置'; $settingsGroupRestored = $restoredSettings.Handle -eq $settingsWindow.Handle -and @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -eq '待操作').Count -eq 1
        $boardPoint = BoardPoint $restoredSettings
        Start-Sleep -Milliseconds ([OutsideProbe]::GetDoubleClickTime()+100)
        [OutsideProbe]::Click($boardPoint.X,$boardPoint.Y); Start-Sleep -Milliseconds 70; [OutsideProbe]::Click($boardPoint.X,$boardPoint.Y)
        Start-Sleep -Milliseconds 25; [OutsideProbe]::Click($boardPoint.X,$boardPoint.Y)
        Start-Sleep -Milliseconds 1000
        $settingsDoubleClosed = @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -eq '即时任务看板设置').Count -eq 0
        $settingsCloseKeepsLists = @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -eq '待操作').Count -eq 1
        # The same external window is still foreground: this click must collapse
        # the lists without requiring a new foreground-change notification.
        [OutsideProbe]::Click($sink.Left+60,$sink.Top+70); Start-Sleep -Milliseconds 500
        $freshOutsideCollapses = @([OutsideProbe]::Windows($taskProcess.Id) | Where-Object Title -eq '待操作').Count -eq 0
        @{ first=$pixels; new=$newPixels; settings=$settingsPixels; settingsSingle=$settingsSinglePixels } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskData 'outside-color-details.json') -Encoding utf8
    }
    $report = [ordered]@{ firstVisible=$firstVisible; samples=$pixels.Samples; firstOutsideMaxColorDifference=$pixels.MaxDifference; slowVisible=$slowVisible; differentVisible=$differentVisible; doubleClosed=$doubleClosed; sharedHost=$sharedHost; newBackgroundDifference=$newBackgroundDifference; settingsSameHost=$settingsSameHost; settingsBackgroundDifference=$settingsBackgroundDifference; settingsSingleVisible=$settingsSingleVisible; settingsSingleDifference=$settingsSingleDifference; settingsDoubleClosed=$settingsDoubleClosed; draftCloseKeepsLists=$draftCloseKeepsLists; settingsCloseKeepsLists=$settingsCloseKeepsLists; freshOutsideCollapses=$freshOutsideCollapses; draftExternalCollapsed=$draftExternalCollapsed; draftGroupRestored=$draftGroupRestored; draftRestoredValue=$draftRestoredValue; settingsExternalCollapsed=$settingsExternalCollapsed; settingsGroupRestored=$settingsGroupRestored; systemDoubleClickMs=[OutsideProbe]::GetDoubleClickTime(); mouseMoved=$true; isolated=$true }
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskData 'outside-click-report.json') -Encoding utf8
    $report | ConvertTo-Json
    if (-not $firstVisible -or ($ExpectDoubleClick -and (-not $slowVisible -or -not $differentVisible -or -not $doubleClosed -or -not $sharedHost -or -not $settingsSameHost -or -not $settingsSingleVisible -or -not $settingsDoubleClosed -or -not $draftCloseKeepsLists -or -not $settingsCloseKeepsLists -or -not $freshOutsideCollapses -or -not $draftExternalCollapsed -or -not $draftGroupRestored -or -not $draftRestoredValue -or -not $settingsExternalCollapsed -or -not $settingsGroupRestored -or $pixels.MaxDifference -ge 35 -or $newBackgroundDifference -ge 35 -or $settingsBackgroundDifference -ge 35 -or $settingsSingleDifference -ge 35))) { throw '外点规则或重复窗口显示验证未通过。' }
} finally {
    if ($taskProcess -and -not $taskProcess.HasExited) { Stop-Process -Id $taskProcess.Id -Force }
    if ($sinkProcess -and -not $sinkProcess.HasExited) { Stop-Process -Id $sinkProcess.Id }
    [OutsideProbe]::Move($cursorBefore.X,$cursorBefore.Y); [OutsideProbe]::SetForegroundWindow($before) | Out-Null
}
