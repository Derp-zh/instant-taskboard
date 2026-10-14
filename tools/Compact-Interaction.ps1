# Dot-sourced by Desktop-Interaction.ps1 only during coordinated isolated acceptance.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
public static class CompactPixelProbe {
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window,IntPtr dc);
  [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc,int x,int y);
  static int Difference(uint a,uint b) => Math.Abs((int)(a&255)-(int)(b&255))+Math.Abs((int)((a>>8)&255)-(int)((b>>8)&255))+Math.Abs((int)((a>>16)&255)-(int)((b>>16)&255));
  public sealed class Result { public int Samples; public int MaxDifference; }
  public static Result During(int x,int y,Action click) {
    using var ready=new ManualResetEventSlim();
    var worker=Task.Run(()=>{
      var result=new Result(); var dc=GetDC(IntPtr.Zero); try {
      var before=GetPixel(dc,x,y); ready.Set(); var watch=Stopwatch.StartNew();
      while(watch.ElapsedMilliseconds<550) {
        var now=GetPixel(dc,x,y);
        result.MaxDifference=Math.Max(result.MaxDifference,Difference(now,before)); result.Samples++; Thread.Sleep(3);
      } return result;
      } finally { ReleaseDC(IntPtr.Zero,dc); }
    });
    if(!ready.Wait(3000)) throw new Exception("像素探测准备超时"); click(); return worker.GetAwaiter().GetResult();
  }
}
'@
function Front-TitleValue($window) {
    $panePixels=[Math]::Min(430,[Math]::Max(80,($workArea.Width/$editorScale-24)/1.4))*$editorScale
    foreach($handle in @([TaskboardMouseProbe]::Children($window.Handle))) {
        $rootElement=[Windows.Automation.AutomationElement]::FromHandle($handle)
        $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Edit)
        $fields=@($rootElement.FindAll([Windows.Automation.TreeScope]::Descendants,$condition) | Where-Object {
            $b=$_.Current.BoundingRectangle
            $b.Width -gt 0 -and $b.Left -ge $window.Bounds.Left -and $b.Right -le $window.Bounds.Left+$panePixels+2 -and $b.Top -gt $window.Bounds.Top+72*$editorScale
        } | Sort-Object { $_.Current.BoundingRectangle.Top })
        if($fields.Count) { return $fields[0].GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value }
    }
    throw '无法读取前台草稿标题'
}
$inspiration=Get-TestWindow '灵感库'
Click-Control (Get-Control $inspiration ([Windows.Automation.ControlType]::Button) '＋ 新建任务')
Click-Control (Get-Control $inspiration ([Windows.Automation.ControlType]::Edit)); [TaskboardMouseProbe]::Type('快捷原生样本')
Click-Control (Get-Control $inspiration ([Windows.Automation.ControlType]::Edit) '' 1); [TaskboardMouseProbe]::Type('详情最后字符')
Start-Sleep -Milliseconds 800
Assert-Test (@((Read-TestData).Tasks | Where-Object Title -eq '快捷原生样本').Count -eq 1) '真实快捷输入自动保存且不重复建任务'
Capture-TestWindow $inspiration 'quick-native-auto.png'
[TaskboardMouseProbe]::Click($inspiration.Bounds.Left+40*$columnScale,$inspiration.Bounds.Top+20*$columnScale)
Start-Sleep -Milliseconds 300
Assert-Test (@((Read-TestData).Tasks | Where-Object Title -eq '快捷原生样本')[0].Stages[0].Text -eq '详情最后字符') '真实点击非焦点标题空白结束快捷录入并保存'
Click-Control (Get-Control $inspiration ([Windows.Automation.ControlType]::Button) '＋ 新建任务')
Click-Control (Get-Control $inspiration ([Windows.Automation.ControlType]::Edit)); [TaskboardMouseProbe]::Type('圆按钮保存样本')
Click-Control (Get-Control $inspiration ([Windows.Automation.ControlType]::Button) '✓')
Start-Sleep -Milliseconds 300
Assert-Test (@((Read-TestData).Tasks | Where-Object Title -eq '圆按钮保存样本').Count -eq 1) '真实详情内圆形✓提交快捷任务'

$running=Get-TestWindow '进行中'
[TaskboardMouseProbe]::Click(($running.Bounds.Left+$running.Bounds.Right)/2,$running.Bounds.Top+65*$columnScale)
$editor=Get-TestWindow '编辑任务'; $editorScale=[TaskboardMouseProbe]::Scale($editor.Handle)
$titleField=Get-Control $editor ([Windows.Automation.ControlType]::Edit)
Click-Control $titleField; [TaskboardMouseProbe]::Key(17,$false); [TaskboardMouseProbe]::Press(65); [TaskboardMouseProbe]::Key(17,$true); [TaskboardMouseProbe]::Type('双外点保留修改')
$sink=[Windows.Forms.Form]::new()
try {
    $sink.Text='即时任务看板隔离外点验收'; $sink.FormBorderStyle='None'; $sink.ShowInTaskbar=$false; $sink.TopMost=$true
    $sink.StartPosition='Manual'; $sink.Location=[Drawing.Point]::new($workArea.Left+20,$workArea.Top+20); $sink.Size=[Drawing.Size]::new(100,70); $sink.BackColor=[Drawing.Color]::SteelBlue
    $sink.Show(); [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 200
    $clickSink=[Action]{ [TaskboardMouseProbe]::Click($sink.Left+50,$sink.Top+35); [Windows.Forms.Application]::DoEvents() }
    $sample=[CompactPixelProbe]::During($editor.Bounds.Left+($editor.Bounds.Right-$editor.Bounds.Left)/2,$editor.Bounds.Bottom-120*$editorScale,$clickSink)
    $sample | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testDirectory 'first-outside-pixels.json')
    Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Handle -eq $editor.Handle).Count -eq 1) '真实外部应用第一次点击保持编辑和看板'
    Assert-Test ($sample.Samples -gt 10 -and $sample.MaxDifference -lt 45) '真实一次外点的背景采样无刷新或白闪'
    Click-Control $titleField
    $clickSink.Invoke()
    Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Handle -eq $editor.Handle).Count -eq 1) '真实内部点击清零后再次外点不关闭'
    $clickSink.Invoke(); Start-Sleep -Milliseconds 350
    Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Handle -eq $editor.Handle).Count -eq 0 -and @((Read-TestData).Sessions | Where-Object { $_.Working.Title -eq '双外点保留修改' -and -not $_.Visible }).Count -eq 1) '真实第二次外点关闭编辑并暂存未提交修改'
} finally { $sink.Close(); $sink.Dispose() }
[TaskboardMouseProbe]::Click(($running.Bounds.Left+$running.Bounds.Right)/2,$running.Bounds.Top+65*$columnScale)
$editor=Get-TestWindow '编辑任务'
Assert-Test ((Get-Control $editor ([Windows.Automation.ControlType]::Edit)).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -eq '双外点保留修改') '真实重开编辑恢复外点暂存内容'
Click-Control (Get-Control $editor ([Windows.Automation.ControlType]::Button) '×')

$ball=Get-TestWindow '即时任务看板入口'
[TaskboardMouseProbe]::Click($ball.Bounds.Right-85*$ballScale,$ball.Bounds.Top+29*$ballScale)
$settingWindow=Get-TestWindow '即时任务看板设置'
$sink=[Windows.Forms.Form]::new()
try {
    $sink.FormBorderStyle='None'; $sink.ShowInTaskbar=$false; $sink.TopMost=$true; $sink.StartPosition='Manual'
    $sink.Location=[Drawing.Point]::new($workArea.Left+20,$workArea.Top+20); $sink.Size=[Drawing.Size]::new(100,70); $sink.Show(); [Windows.Forms.Application]::DoEvents()
    [TaskboardMouseProbe]::Click($sink.Left+50,$sink.Top+35); [Windows.Forms.Application]::DoEvents()
    Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Handle -eq $settingWindow.Handle).Count -eq 1) '真实设置第一次外点保持窗口'
    [TaskboardMouseProbe]::Click($sink.Left+50,$sink.Top+35); [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 200
    Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Handle -eq $settingWindow.Handle).Count -eq 0 -and @([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Title -eq '灵感库').Count -eq 1) '真实设置第二次外点只关闭设置，看板保持'
} finally { $sink.Close(); $sink.Dispose() }

$paused=Get-TestWindow '暂搁置'
Click-Control (Get-Control $paused ([Windows.Automation.ControlType]::Button) '＋ 新建任务')
foreach($unused in 1..2) {
    $draft=Get-TestWindow '新建任务'
    [TaskboardMouseProbe]::Click($draft.Bounds.Left+54*$editorScale,$draft.Bounds.Bottom-35*$editorScale)
}
Assert-Test (@((Read-TestData).Sessions | Where-Object { $null -eq $_.TaskId -and -not $_.Inline }).Count -eq 0) '真实左下删除清理此前暂存草稿'
Click-Control (Get-Control $paused ([Windows.Automation.ControlType]::Button) '＋ 新建任务')
$draft=Get-TestWindow '新建任务'
[TaskboardMouseProbe]::Click($draft.Bounds.Left+95*$editorScale,$draft.Bounds.Top+120*$editorScale); [TaskboardMouseProbe]::Type('原生草稿一')
Start-Sleep -Milliseconds 750
$paneDip=[Math]::Min(430,[Math]::Max(80,($workArea.Width/$editorScale-24)/1.4))
[TaskboardMouseProbe]::Click($draft.Bounds.Left+($paneDip+$paneDip/10)*$editorScale,($draft.Bounds.Top+$draft.Bounds.Bottom)/2)
$draft=Get-TestWindow '新建任务'
[TaskboardMouseProbe]::Click($draft.Bounds.Left+95*$editorScale,$draft.Bounds.Top+120*$editorScale); [TaskboardMouseProbe]::Type('原生草稿二')
Start-Sleep -Milliseconds 750
$draft=Get-TestWindow '新建任务'; Capture-TestWindow $draft 'native-numbered-stack.png'
[TaskboardMouseProbe]::Click($draft.Bounds.Left+($paneDip+$paneDip/10)*$editorScale,$draft.Bounds.Top+100*$editorScale)
Start-Sleep -Milliseconds 350
$draft=Get-TestWindow '新建任务'
Assert-Test ((Front-TitleValue $draft) -eq '原生草稿一') '真实右侧主体切换暂存草稿并恢复输入'
Capture-TestWindow $draft 'native-draft-switched.png'
[TaskboardMouseProbe]::Click($draft.Bounds.Left+($paneDip+$paneDip/10)*$editorScale,$draft.Bounds.Bottom-35*$editorScale)
Start-Sleep -Milliseconds 350
$draft=Get-TestWindow '新建任务'
Assert-Test ((Front-TitleValue $draft) -eq '原生草稿二' -and @((Read-TestData).Tasks | Where-Object Title -like '原生草稿*').Count -eq 0) '真实点击后台保存按钮位置只切换，不提交任务'
[TaskboardMouseProbe]::Click($draft.Bounds.Left+($paneDip+$paneDip/10)*$editorScale,$draft.Bounds.Top+100*$editorScale)
Start-Sleep -Milliseconds 350
$draft=Get-TestWindow '新建任务'
[TaskboardMouseProbe]::Drag($draft.Bounds.Left+($paneDip+$paneDip/10)*$editorScale,$draft.Bounds.Top+36*$editorScale,$draft.Bounds.Left+($paneDip+$paneDip/10)*$editorScale+30,$draft.Bounds.Top+36*$editorScale+15)
$moved=Get-TestWindow '新建任务'
Assert-Test (($moved.Bounds.Left -gt $draft.Bounds.Left+15) -and (Front-TitleValue $moved) -eq '原生草稿一') '真实右侧顶部拖动只移动宿主、不切换草稿'
Click-Control (Get-Control $moved ([Windows.Automation.ControlType]::Button) '×')
Start-Sleep -Milliseconds 250
Assert-Test (@([TaskboardMouseProbe]::Windows($testProcess.Id) | Where-Object Title -eq '新建任务').Count -eq 0 -and @((Read-TestData).Sessions | Where-Object { $null -eq $_.TaskId -and -not $_.Visible }).Count -eq 2) '真实X关闭整个新建宿主，保留两份草稿'
Click-Control (Get-Control $paused ([Windows.Automation.ControlType]::Button) '＋ 新建任务')
$moved=Get-TestWindow '新建任务'
Start-Sleep -Milliseconds 250
Assert-Test ((Front-TitleValue $moved) -eq '原生草稿一') '真实X重开继续原前台草稿'
[TaskboardMouseProbe]::Click($moved.Bounds.Left+54*$editorScale,$moved.Bounds.Bottom-35*$editorScale)
Start-Sleep -Milliseconds 350
$leftDrafts=@((Read-TestData).Sessions | Where-Object { $null -eq $_.TaskId -and -not $_.Inline })
Assert-Test ($leftDrafts.Count -eq 1 -and $leftDrafts[0].Working.Title -eq '原生草稿二') '真实删除当前草稿保留其余创建记录'
$draft=Get-TestWindow '新建任务'
[TaskboardMouseProbe]::Click($draft.Bounds.Left+54*$editorScale,$draft.Bounds.Bottom-35*$editorScale)
Start-Sleep -Milliseconds 300
Assert-Test (@((Read-TestData).Sessions | Where-Object { $null -eq $_.TaskId -and -not $_.Inline }).Count -eq 0) '真实全部删除草稿后清理宿主及暂存'
