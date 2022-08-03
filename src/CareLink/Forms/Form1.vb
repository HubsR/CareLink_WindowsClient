﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.ComponentModel
Imports System.Globalization
Imports System.IO
Imports System.Text.Json
Imports System.Windows.Forms.DataVisualization.Charting

Public Class Form1

#Region "Local variables to hold pump values"

    Private _medicalDeviceBatteryLevelPercent As Integer
    Private _reservoirLevelPercent As Integer
    Private _reservoirRemainingUnits As Double
    Private _sensorDurationHours As Integer
    Private _sensorDurationMinutes As Integer
    Private _sGs As New List(Of SgRecord)
    Private _timeInRange As Integer
    Private _timeToNextCalibHours As UShort = UShort.MaxValue
    Private _timeToNextCalibrationMinutes As Integer

#End Region

    Private Const MilitaryTimeWithMinuteFormat As String = "HH:mm"
    Private Const MilitaryTimeWithoutMinuteFormat As String = "HH:mm"
    Private Const TwelveHourTimeWithMinuteFormat As String = "h:mm tt"
    Private Const TwelveHourTimeWithoutMinuteFormat As String = "h:mm tt"
    Private ReadOnly _bgMiniDisplay As New BGMiniWindow
    Private ReadOnly _calibrationToolTip As New ToolTip()
    Private ReadOnly _insulinImage As Bitmap = My.Resources.InsulinVial_Tiny
    Private ReadOnly _loginDialog As New LoginForm1
    Private ReadOnly _markerInsulinDictionary As New Dictionary(Of Double, Single)
    Private ReadOnly _markerMealDictionary As New Dictionary(Of Double, Single)
    Private ReadOnly _mealImage As Bitmap = My.Resources.MealImage
    Private ReadOnly _thirtySecondInMilliseconds As Integer = CInt(New TimeSpan(0, 0, seconds:=30).TotalMilliseconds)

    Private _activeInsulinIncrements As Integer
    Private _client As CareLinkClient
    Private _filterJsonData As Boolean = True
    Private _initialized As Boolean = False
    Private _inMouseMove As Boolean = False
    Private _limitHigh As Single
    Private _limitLow As Single
    Private _recentDatalast As Dictionary(Of String, String)
    Private _recentDataSameCount As Integer
    Private _timeWithMinuteFormat As String
    Private _timeWithoutMinuteFormat As String
    Private _updating As Boolean = False
    Private Property FormScale As New SizeF(1.0F, 1.0F)
    Private ReadOnly Property SensorLifeToolTip As New ToolTip()
    Public Property BgUnitsString As String
    Public Property RecentData As Dictionary(Of String, String)

#Region "Chart Objects"

#Region "Charts"

    Private WithEvents ActiveInsulinChart As Chart
    Private WithEvents HomeTabChart As Chart
    Private WithEvents HomeTabTimeInRangeChart As Chart

#End Region

#Region "ChartAreas"

    Private WithEvents ActiveInsulinChartArea As ChartArea
    Public WithEvents HomeTabChartArea As ChartArea
    Private WithEvents TimeInRangeChartArea As ChartArea

#End Region

#Region "Legends"

    Private WithEvents ActiveInsulinChartLegend As Legend

#End Region

#Region "Series"

    Private WithEvents ActiveInsulinCurrentBGSeries As Series
    Private WithEvents ActiveInsulinMarkerSeries As Series
    Private WithEvents ActiveInsulinSeries As Series
    Private WithEvents HomeTabCurrentBGSeries As Series
    Private WithEvents HomeTabHighLimitSeries As Series
    Private WithEvents HomeTabLowLimitSeries As Series
    Private WithEvents HomeTabMarkerSeries As Series
    Private WithEvents HomeTabTimeInRangeSeries As New Series

#End Region

#Region "Titles"

    Private WithEvents ActiveInsulinChartTitle As Title

#End Region

    Private _homePageAbsoluteRectangle As RectangleF
    Private _homePageChartRelitivePosition As RectangleF = RectangleF.Empty
    Private _inMenuOptions As Boolean
    Private _insulinRow As Single
    Private _markerRow As Single

    Private Property InsulinRow As Single
        Get
            If _insulinRow = 0 Then
                Throw New ArgumentNullException(NameOf(_insulinRow))
            End If
            Return _insulinRow
        End Get
        Set
            _insulinRow = Value
        End Set
    End Property

    Private Property MarkerRow As Single
        Get
            If _markerRow = 0 Then
                Throw New ArgumentNullException(NameOf(_markerRow))
            End If
            Return _markerRow
        End Get
        Set
            _markerRow = Value
        End Set
    End Property

#End Region

#Region "Events"

#Region "Form Events"

    Private Sub Form1_Closing(sender As Object, e As CancelEventArgs) Handles Me.Closing
        Me.CleanUpNotificationIcon()
    End Sub

    Private Sub Form1_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        Me.CleanUpNotificationIcon()
    End Sub

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles Me.Load
        If My.Settings.UpgradeRequired Then
            My.Settings.Upgrade()
            My.Settings.UpgradeRequired = False
            My.Settings.Save()
        End If

        If My.Settings.UseTestData Then
            Me.MenuOptionsUseLastSavedData.Checked = False
            Me.MenuOptionsUseTestData.Checked = True
        ElseIf My.Settings.UseLastSavedData AndAlso Me.MenuStartHereLoadSavedDataFile.Enabled Then
            Me.MenuOptionsUseLastSavedData.Checked = True
            Me.MenuOptionsUseTestData.Checked = False
        End If
        s_timeZoneList = TimeZoneInfo.GetSystemTimeZones.ToList

        Me.AITComboBox.SelectedIndex = Me.AITComboBox.FindStringExact(My.Settings.AIT.ToString("hh\:mm").Substring(1))
        Me.MenuOptionsUseAdvancedAITDecay.CheckState = If(My.Settings.UseAdvancedAITDecay, CheckState.Checked, CheckState.Unchecked)

    End Sub

    Private Sub Form1_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Me.Fix(Me)

        Me.ShieldUnitsLabel.Parent = Me.ShieldPictureBox
        Me.ShieldUnitsLabel.BackColor = Color.Transparent
        Me.SensorDaysLeftLabel.Parent = Me.SensorTimeLeftPictureBox
        Me.SensorDaysLeftLabel.BackColor = Color.Transparent
        Me.SensorDaysLeftLabel.Left = (Me.SensorTimeLeftPictureBox.Width \ 2) - (Me.SensorDaysLeftLabel.Width \ 2)
        Me.SensorDaysLeftLabel.Top = (Me.SensorTimeLeftPictureBox.Height \ 2) - (Me.SensorDaysLeftLabel.Height \ 2)
        If Me.FormScale.Height > 1 Then
            Me.SplitContainer1.SplitterDistance = 0
        End If
        If Me.DoOptionalLoginAndUpdateData(UpdateAllTabs:=False) Then
            Me.FinishInitialization()
            Me.UpdateAllTabPages()
        End If
    End Sub

    ' Save the current scale value
    ' ScaleControl() is called during the Form's constructor
    Protected Overrides Sub ScaleControl(factor As SizeF, specified As BoundsSpecified)
        Me.FormScale = New SizeF(Me.FormScale.Width * factor.Width, Me.FormScale.Height * factor.Height)
        MyBase.ScaleControl(factor, specified)
    End Sub

#End Region

#Region "Form Menu Events"

#Region "Start Here Menus"

    Private Sub MenuStartHere_DropDownOpened(sender As Object, e As EventArgs) Handles MenuStartHere.DropDownOpened
        Me.MenuStartHereLoadSavedDataFile.Enabled = Directory.GetFiles(MyDocumentsPath, $"{RepoName}*.json").Length > 0
        Me.MenuStartHereSnapshotSave.Enabled = _RecentData IsNot Nothing
        Me.MenuStartHereExceptionReportLoadToolStripMenuItem.Visible = Path.Combine(MyDocumentsPath, $"{RepoErrorReportName}*.txt").Length > 0
    End Sub

    Private Sub MenuStartHereExceptionReportLoadToolStripMenuItem_Click(sender As Object, e As EventArgs) Handles MenuStartHereExceptionReportLoadToolStripMenuItem.Click
        Dim fileList As String() = Directory.GetFiles(MyDocumentsPath, $"{RepoErrorReportName}*.txt")
        Dim openFileDialog1 As New OpenFileDialog With {
            .CheckFileExists = True,
            .CheckPathExists = True,
            .FileName = If(fileList.Length > 0, Path.GetFileName(fileList(0)), RepoName),
            .Filter = $"Error files (*.txt)|{RepoErrorReportName}*.txt",
            .InitialDirectory = MyDocumentsPath,
            .Multiselect = False,
            .ReadOnlyChecked = True,
            .RestoreDirectory = True,
            .SupportMultiDottedExtensions = False,
            .Title = "Select CareLink saved snapshot to load",
            .ValidateNames = True
        }

        If openFileDialog1.ShowDialog() = DialogResult.OK Then
            Try
                Dim fileNameWithPath As String = openFileDialog1.FileName
                Me.ServerUpdateTimer.Stop()
                If File.Exists(fileNameWithPath) Then
                    Me.MenuOptionsUseLastSavedData.CheckState = CheckState.Indeterminate
                    Me.MenuOptionsUseTestData.CheckState = CheckState.Indeterminate
                    ExceptionHandlerForm.ReportFileNameWithPath = fileNameWithPath
                    If ExceptionHandlerForm.ShowDialog() = DialogResult.OK Then
                        ExceptionHandlerForm.ReportFileNameWithPath = ""
                        Me.Text = $"{SavedTitle} Using file {Path.GetFileName(fileNameWithPath)}"
                        Me.RecentData = Loads(ExceptionHandlerForm.LocalRawData)
                        _initialized = False
                        Me.FinishInitialization()
                        Me.UpdateAllTabPages()
                    End If
                End If
            Catch ex As Exception
                MessageBox.Show($"Cannot read file from disk. Original error: {ex.Message}")
            End Try
        End If

    End Sub

    Private Sub MenuStartHereExit_Click(sender As Object, e As EventArgs) Handles StartHereExit.Click
        Me.CleanUpNotificationIcon()
    End Sub

    Private Sub MenuStartHereLoadSavedDataFile_Click(sender As Object, e As EventArgs) Handles MenuStartHereLoadSavedDataFile.Click
        Dim di As New DirectoryInfo(MyDocumentsPath)
        Dim fileList As String() = New DirectoryInfo(MyDocumentsPath).
                                        EnumerateFiles($"{RepoName}*.json").
                                        OrderBy(Function(f As FileInfo) f.LastWriteTime).
                                        Select(Function(f As FileInfo) f.Name).ToArray
        Dim openFileDialog1 As New OpenFileDialog With {
            .CheckFileExists = True,
            .CheckPathExists = True,
            .FileName = If(fileList.Length > 0, fileList.Last, RepoName),
            .Filter = $"json files (*.json)|{RepoName}*.json",
            .InitialDirectory = MyDocumentsPath,
            .Multiselect = False,
            .ReadOnlyChecked = True,
            .RestoreDirectory = True,
            .SupportMultiDottedExtensions = False,
            .Title = "Select CareLink saved snapshot to load",
            .ValidateNames = True
        }

        If openFileDialog1.ShowDialog() = Global.System.Windows.Forms.DialogResult.OK Then
            Try
                If File.Exists(openFileDialog1.FileName) Then
                    Me.ServerUpdateTimer.Stop()
                    Me.MenuOptionsUseLastSavedData.CheckState = CheckState.Indeterminate
                    Me.MenuOptionsUseTestData.CheckState = CheckState.Indeterminate
                    CurrentDateCulture = openFileDialog1.FileName.ExtractCultureFromFileName($"{RepoName}", True)

                    _RecentData = Loads(File.ReadAllText(openFileDialog1.FileName))
                    _initialized = False
                    Me.FinishInitialization()
                    Me.Text = $"{SavedTitle} Using file {Path.GetFileName(openFileDialog1.FileName)}"
                    Me.UpdateAllTabPages()
                End If
            Catch ex As Exception
                MessageBox.Show($"Cannot read file from disk. Original error: {ex.Message}")
            End Try
        End If
    End Sub

    Private Sub MenuStartHereLogin_Click(sender As Object, e As EventArgs) Handles MenuStartHereLogin.Click
        Me.MenuOptionsUseTestData.CheckState = CheckState.Indeterminate
        Me.MenuOptionsUseLastSavedData.CheckState = CheckState.Indeterminate
        Me.DoOptionalLoginAndUpdateData(UpdateAllTabs:=True)
    End Sub

    Private Sub MenuStartHereSnapshotSave_Click(sender As Object, e As EventArgs) Handles MenuStartHereSnapshotSave.Click
        Using jd As JsonDocument = JsonDocument.Parse(_RecentData.CleanUserData(), New JsonDocumentOptions)
            File.WriteAllText(GetDataFileName(RepoSnapshotName, CurrentDateCulture.Name, "json", True).withPath, JsonSerializer.Serialize(jd, JsonFormattingOptions))
        End Using
    End Sub

#End Region

#Region "Option Menus"

    Private Sub MenuOptionsFilterRawJSONData_Click(sender As Object, e As EventArgs) Handles MenuOptionsFilterRawJSONData.Click
        _filterJsonData = Me.MenuOptionsFilterRawJSONData.Checked
    End Sub

    Private Sub MenuOptionsSetupEmailServer_Click(sender As Object, e As EventArgs) Handles MenuOptionsSetupEmailServer.Click
        MailSetupDialog.ShowDialog()
    End Sub

    Private Sub MenuOptionsUseAdvancedAITDecay_CheckStateChanged(sender As Object, e As EventArgs) Handles MenuOptionsUseAdvancedAITDecay.CheckStateChanged
        Dim increments As Double = TimeSpan.Parse(My.Settings.AIT.ToString("hh\:mm").Substring(1)) / s_fiveMinuteSpan
        If Me.MenuOptionsUseAdvancedAITDecay.Checked Then
            _activeInsulinIncrements = CInt(increments * 1.4)
            My.Settings.UseAdvancedAITDecay = True
            Me.AITLabel.Text = "Advanced AIT Decay"
        Else
            _activeInsulinIncrements = CInt(increments)
            My.Settings.UseAdvancedAITDecay = False
            Me.AITLabel.Text = "Active Insulin Time"
        End If
        My.Settings.Save()
        Me.UpdateActiveInsulinChart()

    End Sub

    Private Sub MenuOptionsUseLastSavedData_CheckStateChanged(sender As Object, e As EventArgs) Handles MenuOptionsUseLastSavedData.CheckStateChanged
        If _inMenuOptions Then Exit Sub
        Select Case Me.MenuOptionsUseLastSavedData.CheckState
            Case CheckState.Checked
                Me.MenuOptionsUseTestData.CheckState = CheckState.Indeterminate
                My.Settings.UseLastSavedData = True
                My.Settings.UseTestData = False
                Me.DoOptionalLoginAndUpdateData(UpdateAllTabs:=True)
            Case CheckState.Unchecked
                My.Settings.UseLastSavedData = False
                If _initialized AndAlso Not (Me.MenuOptionsUseTestData.Checked OrElse Me.MenuOptionsUseLastSavedData.Checked) Then
                    Me.DoOptionalLoginAndUpdateData(UpdateAllTabs:=True)
                End If
            Case CheckState.Indeterminate
                _inMenuOptions = True
                My.Settings.UseLastSavedData = False
                Me.MenuOptionsUseLastSavedData.Checked = False
                _inMenuOptions = False
        End Select
        Me.MenuStartHereSnapshotSave.Enabled = Me.MenuOptionsUseLastSavedData.Checked

        My.Settings.Save()
    End Sub

    Private Sub MenuOptionsUseTestData_Checkchange(sender As Object, e As EventArgs) Handles MenuOptionsUseTestData.CheckStateChanged
        If _inMenuOptions Then Exit Sub
        Select Case Me.MenuOptionsUseTestData.CheckState
            Case CheckState.Checked
                Me.MenuOptionsUseLastSavedData.CheckState = CheckState.Indeterminate
                My.Settings.UseLastSavedData = False
                My.Settings.UseTestData = True
                Me.DoOptionalLoginAndUpdateData(UpdateAllTabs:=True)
            Case CheckState.Unchecked
                My.Settings.UseTestData = False
                If _initialized AndAlso Not Me.MenuOptionsUseLastSavedData.Checked Then
                    Me.DoOptionalLoginAndUpdateData(UpdateAllTabs:=True)
                End If
            Case CheckState.Indeterminate
                _inMenuOptions = True
                My.Settings.UseTestData = False
                Me.MenuOptionsUseTestData.Checked = False
                _inMenuOptions = False
        End Select
        My.Settings.Save()
    End Sub

#End Region

#Region "View Menus"

    Private Sub MenuViewShowMiniDisplay_Click(sender As Object, e As EventArgs) Handles MenuViewShowMiniDisplay.Click
        Me.Hide()
        _bgMiniDisplay.Show()
    End Sub

#End Region

#Region "Help Menus"

    Private Sub MenuHelpAbout_Click(sender As Object, e As EventArgs) Handles MenuHelpAbout.Click
        AboutBox1.Show()
    End Sub

    Private Sub MenuHelpCheckForUpdates_Click(sender As Object, e As EventArgs) Handles MenuHelpCheckForUpdatesMenuItem.Click
        CheckForUpdatesAsync(Me, reportResults:=True)
    End Sub

    Private Sub MenuHelpReportIssueMenuItem_Click(sender As Object, e As EventArgs) Handles MenuHelpReportAProblem.Click
        OpenUrlInBrowser($"{GitHubCareLinkUrl}issues")
    End Sub

#End Region

#Region "HomePage Tab Events"

    Private Sub AITComboBox_SelectedIndexChanged(sender As Object, e As EventArgs) Handles AITComboBox.SelectedIndexChanged
        Dim aitTimeSpan As TimeSpan = TimeSpan.Parse(Me.AITComboBox.SelectedItem.ToString())
        My.Settings.AIT = aitTimeSpan
        My.Settings.Save()
        _activeInsulinIncrements = CInt(TimeSpan.Parse(aitTimeSpan.ToString("hh\:mm").Substring(1)) / s_fiveMinuteSpan)
        Me.UpdateActiveInsulinChart()
    End Sub

    Private Sub CalibrationDueImage_MouseHover(sender As Object, e As EventArgs) Handles CalibrationDueImage.MouseHover
        If _timeToNextCalibrationMinutes > 0 AndAlso _timeToNextCalibrationMinutes < 1440 Then
            _calibrationToolTip.SetToolTip(Me.CalibrationDueImage, $"Calibration Due {Now.AddMinutes(_timeToNextCalibrationMinutes).ToShortTimeString}")
        End If
    End Sub

#Region "Home Page Chart Events"

    Private Sub HomePageChart_CursorPositionChanging(sender As Object, e As CursorEventArgs) Handles HomeTabChart.CursorPositionChanging
        If Not _initialized Then Exit Sub

        Me.CursorTimer.Interval = _thirtySecondInMilliseconds
        Me.CursorTimer.Start()
    End Sub

    Private Sub HomePageChart_MouseMove(sender As Object, e As MouseEventArgs) Handles HomeTabChart.MouseMove

        If Not _initialized Then
            Exit Sub
        End If
        _inMouseMove = True
        Dim yInPixels As Double = Me.HomeTabChart.ChartAreas(NameOf(HomeTabChartArea)).AxisY2.ValueToPixelPosition(e.Y)
        If Double.IsNaN(yInPixels) Then
            Exit Sub
        End If
        Dim result As HitTestResult
        Try
            result = Me.HomeTabChart.HitTest(e.X, e.Y)
            If result?.PointIndex >= -1 Then
                If result.Series IsNot Nothing Then
                    Me.CursorTimeLabel.Left = e.X - (Me.CursorTimeLabel.Width \ 2)
                    Select Case result.Series.Name
                        Case NameOf(HomeTabHighLimitSeries), NameOf(HomeTabLowLimitSeries)
                            Me.CursorMessage1Label.Visible = False
                            Me.CursorMessage2Label.Visible = False
                            Me.CursorPictureBox.Image = Nothing
                            Me.CursorTimeLabel.Visible = False
                            Me.CursorValueLabel.Visible = False
                        Case NameOf(HomeTabMarkerSeries)
                            Dim markerToolTip() As String = result.Series.Points(result.PointIndex).ToolTip.Split(":"c)
                            Dim xValue As Date = Date.FromOADate(result.Series.Points(result.PointIndex).XValue)
                            Me.CursorTimeLabel.Visible = True
                            Me.CursorTimeLabel.Text = xValue.ToString(_timeWithMinuteFormat)
                            Me.CursorTimeLabel.Tag = xValue
                            markerToolTip(0) = markerToolTip(0).Trim
                            Me.CursorValueLabel.Visible = True
                            Me.CursorPictureBox.SizeMode = PictureBoxSizeMode.StretchImage
                            Me.CursorPictureBox.Visible = True
                            Select Case markerToolTip.Length
                                Case 2
                                    Me.CursorMessage1Label.Text = markerToolTip(0)
                                    Select Case markerToolTip(0)
                                        Case "Auto Correction", "Basal", "Bolus"
                                            Me.CursorPictureBox.Image = My.Resources.InsulinVial
                                            Me.CursorMessage1Label.Visible = True
                                        Case "Meal"
                                            Me.CursorPictureBox.Image = My.Resources.MealImageLarge
                                            Me.CursorMessage1Label.Visible = True
                                        Case Else
                                            Me.CursorPictureBox.Image = Nothing
                                            Me.CursorMessage1Label.Visible = False

                                    End Select
                                    Me.CursorMessage2Label.Visible = False
                                    Me.CursorValueLabel.Top = Me.CursorMessage1Label.PositionBelow
                                    Me.CursorValueLabel.Text = markerToolTip(1).Trim
                                Case 3
                                    Select Case markerToolTip(1).Trim
                                        Case "Calibration accepted", "Calibration not accepted"
                                            Me.CursorPictureBox.Image = My.Resources.CalibrationDotRed
                                        Case "Not used For calibration"
                                            Me.CursorPictureBox.Image = My.Resources.CalibrationDot
                                        Case Else
                                            Stop
                                    End Select
                                    Me.CursorMessage1Label.Text = markerToolTip(0)
                                    Me.CursorMessage1Label.Top = Me.CursorPictureBox.PositionBelow
                                    Me.CursorMessage1Label.Visible = True
                                    Me.CursorMessage2Label.Text = markerToolTip(1).Trim
                                    Me.CursorMessage2Label.Top = Me.CursorMessage1Label.PositionBelow
                                    Me.CursorMessage2Label.Visible = True
                                    Me.CursorValueLabel.Text = markerToolTip(2).Trim
                                    Me.CursorValueLabel.Top = Me.CursorMessage2Label.PositionBelow
                                Case Else
                                    Stop
                            End Select
                        Case NameOf(HomeTabCurrentBGSeries)
                            Me.CursorPictureBox.Image = Nothing
                            Me.CursorMessage1Label.Visible = False
                            Me.CursorMessage2Label.Visible = False
                            Me.CursorValueLabel.Visible = False
                            Me.CursorTimeLabel.Text = Date.FromOADate(result.Series.Points(result.PointIndex).XValue).ToString(_timeWithMinuteFormat)
                            Me.CursorTimeLabel.Visible = True
                            Me.CursorMessage1Label.Text = $"{result.Series.Points(result.PointIndex).YValues(0).RoundDouble(3)} {Me.BgUnitsString}"
                            Me.CursorMessage1Label.Visible = True
                    End Select
                End If
            Else
                Me.CursorMessage1Label.Visible = False
                Me.CursorMessage2Label.Visible = False
                Me.CursorPictureBox.Image = Nothing
                Me.CursorTimeLabel.Visible = False
                Me.CursorValueLabel.Visible = False
            End If
        Catch ex As Exception
            result = Nothing
        Finally
            _inMouseMove = False
        End Try
    End Sub

    '<DebuggerNonUserCode()>
    Private Sub HomePageChart_PostPaint(sender As Object, e As ChartPaintEventArgs) Handles HomeTabChart.PostPaint
        If Not _initialized OrElse _updating OrElse _inMouseMove Then
            Exit Sub
        End If
        If _homePageChartRelitivePosition.IsEmpty Then
            _homePageChartRelitivePosition.X = CSng(e.ChartGraphics.GetPositionFromAxis(NameOf(HomeTabChartArea), AxisName.X, _sGs(0).OADate))
            _homePageChartRelitivePosition.Y = CSng(e.ChartGraphics.GetPositionFromAxis(NameOf(HomeTabChartArea), AxisName.Y, _markerRow))
            _homePageChartRelitivePosition.Height = CSng(e.ChartGraphics.GetPositionFromAxis(NameOf(HomeTabChartArea), AxisName.Y, CSng(e.ChartGraphics.GetPositionFromAxis(NameOf(HomeTabChartArea), AxisName.Y, _limitHigh)))) - _homePageChartRelitivePosition.Y
            _homePageChartRelitivePosition.Width = CSng(e.ChartGraphics.GetPositionFromAxis(NameOf(HomeTabChartArea), AxisName.X, _sGs.Last.OADate)) - _homePageChartRelitivePosition.X
            _homePageChartRelitivePosition = e.ChartGraphics.GetAbsoluteRectangle(_homePageChartRelitivePosition)
        End If

        Dim homePageChartY As Integer = CInt(_homePageChartRelitivePosition.Y)
        Dim homePageChartWidth As Integer = CInt(_homePageChartRelitivePosition.Width)
        Dim highLimitY As Double = e.ChartGraphics.GetPositionFromAxis(NameOf(HomeTabChartArea), AxisName.Y, _limitHigh)
        Dim lowLimitY As Double = e.ChartGraphics.GetPositionFromAxis(NameOf(HomeTabChartArea), AxisName.Y, _limitLow)

        Using b As New SolidBrush(Color.FromArgb(30, Color.Black))
            Dim highHeight As Integer = CInt(255 * Me.FormScale.Height)
            Dim homePagelocation As New Point(CInt(_homePageChartRelitivePosition.X), homePageChartY)
            Dim highAreaRectangle As New Rectangle(homePagelocation,
                                                   New Size(homePageChartWidth, highHeight))
            e.ChartGraphics.Graphics.FillRectangle(b, highAreaRectangle)
        End Using

        Using b As New SolidBrush(Color.FromArgb(30, Color.Black))
            Dim lowOffset As Integer = CInt((10 + _homePageChartRelitivePosition.Height) * Me.FormScale.Height)
            Dim lowStartLocation As New Point(CInt(_homePageChartRelitivePosition.X), lowOffset)

            Dim lowRawHeight As Integer = CInt((50 - homePageChartY) * Me.FormScale.Height)
            Dim lowHeight As Integer = If(Me.HomeTabChartArea.AxisX.ScrollBar.IsVisible,
                                          CInt(lowRawHeight - Me.HomeTabChartArea.AxisX.ScrollBar.Size),
                                          lowRawHeight
                                         )
            Dim lowAreaRectangle As New Rectangle(lowStartLocation,
                                                  New Size(homePageChartWidth, lowHeight))
            e.ChartGraphics.Graphics.FillRectangle(b, lowAreaRectangle)
        End Using
        If Me.CursorTimeLabel.Tag IsNot Nothing Then
            Me.CursorTimeLabel.Left = CInt(e.ChartGraphics.GetPositionFromAxis(NameOf(HomeTabChartArea), AxisName.X, Me.CursorTimeLabel.Tag.ToString.ParseDate("").ToOADate))
        End If

        e.PaintMarker(_mealImage, _markerMealDictionary, 0)
        e.PaintMarker(_insulinImage, _markerInsulinDictionary, -6)
    End Sub

    Private Sub SensorAgeLeftLabel_MouseHover(sender As Object, e As EventArgs) Handles SensorDaysLeftLabel.MouseHover
        If _sensorDurationHours < 24 Then
            Me.SensorLifeToolTip.SetToolTip(Me.CalibrationDueImage, $"Sensor will expire in {_sensorDurationHours} hours")
        End If
    End Sub

#End Region

#End Region

#Region "SGS Tab Events"

    Private Sub SGsDataGridView_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs) Handles SGsDataGridView.CellFormatting
        ' Set the background to red for negative values in the Balance column.
        If Me.SGsDataGridView.Columns(e.ColumnIndex).Name.Equals(NameOf(s_sensorState), StringComparison.OrdinalIgnoreCase) Then
            If CStr(e.Value) <> "NO_ERROR_MESSAGE" Then
                e.CellStyle.BackColor = Color.Yellow
            End If
        End If
        If Me.SGsDataGridView.Columns(e.ColumnIndex).Name.Equals(NameOf(DateTime), StringComparison.OrdinalIgnoreCase) Then
            If e.Value IsNot Nothing Then
                Dim dateValue As Date = e.Value.ToString.ParseDate("")
                e.Value = $"{dateValue.ToShortDateString()} {dateValue.ToShortTimeString()}"
            End If
        End If
        If Me.SGsDataGridView.Columns(e.ColumnIndex).Name.Equals(NameOf(SgRecord.sg), StringComparison.OrdinalIgnoreCase) Then
            If e.Value IsNot Nothing Then
                Dim sendorValue As Single = CSng(e.Value)
                If Single.IsNaN(sendorValue) Then
                    e.CellStyle.BackColor = Color.Gray
                ElseIf sendorValue < 70 Then
                    e.CellStyle.BackColor = Color.Red
                ElseIf sendorValue > 180 Then
                    e.CellStyle.BackColor = Color.Orange
                End If
            End If
        End If

    End Sub

    Private Sub SGsDataGridView_ColumnAdded(sender As Object, e As DataGridViewColumnEventArgs) Handles SGsDataGridView.ColumnAdded
        With e.Column
            If .Name = NameOf(SgRecord.OADate) Then
                .Visible = False
                Exit Sub
            End If
            .AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells
            .ReadOnly = True
            .Resizable = DataGridViewTriState.False
            .HeaderText = .Name.ToTitleCase()
            .DefaultCellStyle = SgRecord.GetCellStyle(.Name)
            If .Name <> NameOf(SgRecord.RecordNumber) Then
                .SortMode = DataGridViewColumnSortMode.NotSortable
            End If
        End With
    End Sub

    Private Sub SGsDataGridView_ColumnHeaderMouseClick(sender As Object, e As DataGridViewCellMouseEventArgs) Handles SGsDataGridView.ColumnHeaderMouseClick
        Dim currentSortOrder As SortOrder = Me.SGsDataGridView.Columns(e.ColumnIndex).HeaderCell.SortGlyphDirection
        If Me.SGsDataGridView.Columns(e.ColumnIndex).Name = NameOf(SgRecord.RecordNumber) Then
            If currentSortOrder = SortOrder.None OrElse currentSortOrder = SortOrder.Ascending Then
                Me.SGsDataGridView.DataSource = _sGs.OrderByDescending(Function(x) x.RecordNumber).ToList
                currentSortOrder = SortOrder.Descending
            Else
                Me.SGsDataGridView.DataSource = _sGs.OrderBy(Function(x) x.RecordNumber).ToList
                currentSortOrder = SortOrder.Ascending
            End If
        End If
        Me.SGsDataGridView.Columns(e.ColumnIndex).HeaderCell.SortGlyphDirection = currentSortOrder
    End Sub

#End Region

#End Region

#Region "Timer Events"

    Private Sub CursorTimer_Tick(sender As Object, e As EventArgs) Handles CursorTimer.Tick
        If Not Me.HomeTabChartArea.AxisX.ScaleView.IsZoomed Then
            Me.CursorTimer.Enabled = False
            Me.HomeTabChartArea.CursorX.Position = Double.NaN
        End If
    End Sub

    Private Sub ServerUpdateTimer_Tick(sender As Object, e As EventArgs) Handles ServerUpdateTimer.Tick
        Me.ServerUpdateTimer.Stop()
        _RecentData = _client.GetRecentData()
        If Me.IsRecentDataUpdated Then
            Me.UpdateAllTabPages()
        ElseIf _RecentData Is Nothing Then
            _client = New CareLinkClient(Me.LoginStatus, My.Settings.CareLinkUserName, My.Settings.CareLinkPassword, My.Settings.CountryCode)
            _loginDialog.Client = _client
            _RecentData = _client.GetRecentData()
            If Me.IsRecentDataUpdated Then
                Me.UpdateAllTabPages()
            End If
        End If
        Application.DoEvents()
        Me.ServerUpdateTimer.Interval = CType(New TimeSpan(0, minutes:=1, 0).TotalMilliseconds, Integer)
        Me.ServerUpdateTimer.Start()
        Debug.Print($"Me.ServerUpdateTimer Started at {Now}")
        Me.Cursor = Cursors.Default
    End Sub

#End Region ' Timer

#End Region ' Events

#Region "Initialize Charts"

    Private Sub InitializeActiveInsulinTabChart()
        Me.ActiveInsulinChart = New Chart With {
            .Anchor = AnchorStyles.Left Or AnchorStyles.Right,
            .BackColor = Color.WhiteSmoke,
            .BackGradientStyle = GradientStyle.TopBottom,
            .BackSecondaryColor = Color.White,
            .BorderlineColor = Color.FromArgb(26, 59, 105),
            .BorderlineDashStyle = ChartDashStyle.Solid,
            .BorderlineWidth = 2,
            .Dock = DockStyle.Fill,
            .Name = NameOf(ActiveInsulinChart),
            .TabIndex = 0
        }

        Me.ActiveInsulinChartArea = New ChartArea With {
            .BackColor = Color.FromArgb(180, 23, 47, 19),
            .BackGradientStyle = GradientStyle.TopBottom,
            .BackSecondaryColor = Color.FromArgb(180, 29, 56, 26),
            .BorderColor = Color.FromArgb(64, 64, 64, 64),
            .BorderDashStyle = ChartDashStyle.Solid,
            .Name = NameOf(ActiveInsulinChartArea),
            .ShadowColor = Color.Transparent
        }

        With Me.ActiveInsulinChartArea
            With .AxisX
                .Interval = 2
                .IntervalType = DateTimeIntervalType.Hours
                .IsInterlaced = True
                .IsMarginVisible = True
                .LabelAutoFitStyle = LabelAutoFitStyles.IncreaseFont Or LabelAutoFitStyles.DecreaseFont Or LabelAutoFitStyles.WordWrap
                With .LabelStyle
                    .Font = New Font("Trebuchet MS", 8.25F, FontStyle.Bold)
                    .Format = _timeWithoutMinuteFormat
                End With
                .LineColor = Color.FromArgb(64, 64, 64, 64)
                .MajorGrid.LineColor = Color.FromArgb(64, 64, 64, 64)
                .ScaleView.Zoomable = True
                With .ScrollBar
                    .BackColor = Color.White
                    .ButtonColor = Color.Lime
                    .IsPositionedInside = True
                    .LineColor = Color.Black
                    .Size = 15
                End With
            End With
            With .AxisY
                .InterlacedColor = Color.FromArgb(120, Color.LightSlateGray)
                .Interval = 2
                .IntervalAutoMode = IntervalAutoMode.FixedCount
                .IsInterlaced = True
                .IsLabelAutoFit = False
                .IsMarginVisible = False
                .IsStartedFromZero = True
                .LabelStyle.Font = New Font("Trebuchet MS", 8.25F, FontStyle.Bold)
                .LineColor = Color.FromArgb(64, 64, 64, 64)
                .MajorGrid.LineColor = Color.FromArgb(64, 64, 64, 64)
                .MajorTickMark = New TickMark() With {.Interval = Me.InsulinRow, .Enabled = False}
                .Maximum = 25
                .Minimum = 0
                .ScaleView.Zoomable = False
                .Title = "Active Insulin"
                .TitleForeColor = Color.HotPink
            End With
            With .AxisY2
                .Maximum = Me.MarkerRow
                .Minimum = 0
                .Title = "BG Value"
            End With
            With .CursorX
                .AutoScroll = True
                .AxisType = AxisType.Primary
                .Interval = 0
                .IsUserEnabled = True
                .IsUserSelectionEnabled = True
            End With
            With .CursorY
                .AutoScroll = False
                .AxisType = AxisType.Secondary
                .Interval = 0
                .IsUserEnabled = False
                .IsUserSelectionEnabled = False
                .LineColor = Color.Transparent
            End With
        End With

        Me.ActiveInsulinChart.ChartAreas.Add(Me.ActiveInsulinChartArea)

        Me.ActiveInsulinChartLegend = New Legend With {
            .BackColor = Color.Transparent,
            .Enabled = False,
            .Font = New Font("Trebuchet MS", 8.25F, FontStyle.Bold),
            .IsTextAutoFit = False,
            .Name = NameOf(ActiveInsulinChartLegend)
        }
        Me.ActiveInsulinChart.Legends.Add(Me.ActiveInsulinChartLegend)
        Me.ActiveInsulinSeries = New Series With {
            .BorderColor = Color.FromArgb(180, 26, 59, 105),
            .BorderWidth = 4,
            .ChartArea = NameOf(ActiveInsulinChartArea),
            .ChartType = SeriesChartType.Line,
            .Color = Color.HotPink,
            .Legend = NameOf(ActiveInsulinChartLegend),
            .Name = NameOf(ActiveInsulinSeries),
            .ShadowColor = Color.Black,
            .XValueType = ChartValueType.DateTime,
            .YAxisType = AxisType.Primary
        }
        Me.ActiveInsulinCurrentBGSeries = New Series With {
            .BorderColor = Color.FromArgb(180, 26, 59, 105),
            .BorderWidth = 4,
            .ChartArea = NameOf(ActiveInsulinChartArea),
            .ChartType = SeriesChartType.Line,
            .Color = Color.Blue,
            .Legend = NameOf(ActiveInsulinChartLegend),
            .Name = NameOf(ActiveInsulinCurrentBGSeries),
            .ShadowColor = Color.Black,
            .XValueType = ChartValueType.DateTime,
            .YAxisType = AxisType.Secondary
        }
        Me.ActiveInsulinMarkerSeries = New Series With {
            .BorderColor = Color.Transparent,
            .BorderWidth = 1,
            .ChartArea = NameOf(ActiveInsulinChartArea),
            .ChartType = SeriesChartType.Point,
            .Color = Color.HotPink,
            .Name = NameOf(ActiveInsulinMarkerSeries),
            .MarkerSize = 8,
            .MarkerStyle = MarkerStyle.Circle,
            .XValueType = ChartValueType.DateTime,
            .YAxisType = AxisType.Primary
        }

        Me.ActiveInsulinChart.Series.Add(Me.ActiveInsulinSeries)
        Me.ActiveInsulinChart.Series.Add(Me.ActiveInsulinCurrentBGSeries)
        Me.ActiveInsulinChart.Series.Add(Me.ActiveInsulinMarkerSeries)

        Me.ActiveInsulinChart.Series(NameOf(ActiveInsulinSeries)).EmptyPointStyle.Color = Color.Transparent
        Me.ActiveInsulinChart.Series(NameOf(ActiveInsulinSeries)).EmptyPointStyle.BorderWidth = 4
        Me.ActiveInsulinChart.Series(NameOf(ActiveInsulinCurrentBGSeries)).EmptyPointStyle.Color = Color.Transparent
        Me.ActiveInsulinChart.Series(NameOf(ActiveInsulinCurrentBGSeries)).EmptyPointStyle.BorderWidth = 4
        Me.ActiveInsulinChartTitle = New Title With {
                .Font = New Font("Trebuchet MS", 12.0F, FontStyle.Bold),
                .ForeColor = Color.FromArgb(26, 59, 105),
                .Name = NameOf(ActiveInsulinChartTitle),
                .ShadowColor = Color.FromArgb(32, 0, 0, 0),
                .ShadowOffset = 3
            }
        Me.ActiveInsulinChart.Titles.Add(Me.ActiveInsulinChartTitle)
        Me.TabPage2RunningActiveInsulin.Controls.Add(Me.ActiveInsulinChart)
        Application.DoEvents()

    End Sub

    Private Sub InitializeHomePageChart()
        Me.HomeTabChart = New Chart With {
             .Anchor = AnchorStyles.Left Or AnchorStyles.Right,
             .BackColor = Color.WhiteSmoke,
             .BackGradientStyle = GradientStyle.TopBottom,
             .BackSecondaryColor = Color.White,
             .BorderlineColor = Color.FromArgb(26, 59, 105),
             .BorderlineDashStyle = ChartDashStyle.Solid,
             .BorderlineWidth = 2,
             .Dock = DockStyle.Fill,
             .Name = NameOf(HomeTabChart),
             .TabIndex = 0
         }

        Me.HomeTabChartArea = New ChartArea With {
             .BackColor = Color.FromArgb(180, 23, 47, 19),
             .BackGradientStyle = GradientStyle.TopBottom,
             .BackSecondaryColor = Color.FromArgb(180, 29, 56, 26),
             .BorderColor = Color.FromArgb(64, 64, 64, 64),
             .BorderDashStyle = ChartDashStyle.Solid,
             .Name = NameOf(HomeTabChartArea),
             .ShadowColor = Color.Transparent
         }
        With Me.HomeTabChartArea
            With .AxisX
                .Interval = 2
                .IntervalType = DateTimeIntervalType.Hours
                .IsInterlaced = True
                .IsMarginVisible = True
                .LabelAutoFitStyle = LabelAutoFitStyles.IncreaseFont Or LabelAutoFitStyles.DecreaseFont Or LabelAutoFitStyles.WordWrap
                With .LabelStyle
                    .Font = New Font("Trebuchet MS", 8.25F, FontStyle.Bold)
                    .Format = _timeWithoutMinuteFormat
                End With
                .LineColor = Color.FromArgb(64, 64, 64, 64)
                .MajorGrid.LineColor = Color.FromArgb(64, 64, 64, 64)
                .ScaleView.Zoomable = True
                With .ScrollBar
                    .BackColor = Color.White
                    .ButtonColor = Color.Lime
                    .IsPositionedInside = True
                    .LineColor = Color.Black
                    .Size = 15
                End With
            End With
            With .AxisY
                .InterlacedColor = Color.FromArgb(120, Color.LightSlateGray)
                .Interval = Me.InsulinRow
                .IntervalAutoMode = IntervalAutoMode.FixedCount
                .IsInterlaced = True
                .IsLabelAutoFit = False
                .IsMarginVisible = False
                .IsStartedFromZero = False
                .LabelStyle.Font = New Font("Trebuchet MS", 8.25F, FontStyle.Bold)
                .LineColor = Color.FromArgb(64, 64, 64, 64)
                .MajorGrid.LineColor = Color.FromArgb(64, 64, 64, 64)
                .MajorTickMark = New TickMark() With {.Interval = Me.InsulinRow, .Enabled = False}
                .Maximum = Me.MarkerRow
                .Minimum = Me.InsulinRow
                .ScaleBreakStyle = New AxisScaleBreakStyle() With {
                        .Enabled = True,
                        .StartFromZero = StartFromZero.No,
                        .BreakLineStyle = BreakLineStyle.Straight
                    }
                .ScaleView.Zoomable = False
            End With
            With .AxisY2
                .Interval = Me.InsulinRow
                .IsMarginVisible = False
                .IsStartedFromZero = False
                .LabelStyle.Font = New Font("Trebuchet MS", 8.25F, FontStyle.Bold)
                .LineColor = Color.FromArgb(64, 64, 64, 64)
                .MajorGrid = New Grid With {
                        .Interval = Me.InsulinRow,
                        .LineColor = Color.FromArgb(64, 64, 64, 64)
                    }
                .MajorTickMark = New TickMark() With {.Interval = Me.InsulinRow, .Enabled = True}
                .Maximum = Me.MarkerRow
                .Minimum = Me.InsulinRow
                .ScaleView.Zoomable = False
            End With
            With .CursorX
                .AutoScroll = True
                .AxisType = AxisType.Primary
                .Interval = 0
                .IsUserEnabled = True
                .IsUserSelectionEnabled = True
            End With
            With .CursorY
                .AutoScroll = False
                .AxisType = AxisType.Secondary
                .Interval = 0
                .IsUserEnabled = False
                .IsUserSelectionEnabled = False
                .LineColor = Color.Transparent
            End With

        End With

        Me.HomeTabChart.ChartAreas.Add(Me.HomeTabChartArea)

        Dim defaultLegend As New Legend With {
                .BackColor = Color.Transparent,
                .Enabled = False,
                .Font = New Font("Trebuchet MS", 8.25F, FontStyle.Bold),
                .IsTextAutoFit = False,
                .Name = NameOf(defaultLegend)
            }
        Me.HomeTabCurrentBGSeries = New Series With {
                .BorderColor = Color.FromArgb(180, 26, 59, 105),
                .BorderWidth = 4,
                .ChartArea = NameOf(HomeTabChartArea),
                .ChartType = SeriesChartType.Line,
                .Color = Color.White,
                .Legend = NameOf(defaultLegend),
                .Name = NameOf(HomeTabCurrentBGSeries),
                .ShadowColor = Color.Black,
                .XValueType = ChartValueType.DateTime,
                .YAxisType = AxisType.Secondary
            }
        Me.HomeTabMarkerSeries = New Series With {
                .BorderColor = Color.Transparent,
                .BorderWidth = 1,
                .ChartArea = NameOf(HomeTabChartArea),
                .ChartType = SeriesChartType.Point,
                .Color = Color.HotPink,
                .Name = NameOf(HomeTabMarkerSeries),
                .MarkerSize = 12,
                .MarkerStyle = MarkerStyle.Circle,
                .XValueType = ChartValueType.DateTime,
                .YAxisType = AxisType.Secondary
            }

        Me.HomeTabHighLimitSeries = New Series With {
                .BorderColor = Color.FromArgb(180, Color.Orange),
                .BorderWidth = 2,
                .ChartArea = NameOf(HomeTabChartArea),
                .ChartType = SeriesChartType.StepLine,
                .Color = Color.Orange,
                .Name = NameOf(HomeTabHighLimitSeries),
                .ShadowColor = Color.Black,
                .XValueType = ChartValueType.DateTime,
                .YAxisType = AxisType.Secondary
            }
        Me.HomeTabLowLimitSeries = New Series With {
                .BorderColor = Color.FromArgb(180, Color.Red),
                .BorderWidth = 2,
                .ChartArea = NameOf(HomeTabChartArea),
                .ChartType = SeriesChartType.StepLine,
                .Color = Color.Red,
                .Name = NameOf(HomeTabLowLimitSeries),
                .ShadowColor = Color.Black,
                .XValueType = ChartValueType.DateTime,
                .YAxisType = AxisType.Secondary
            }

        Me.SplitContainer3.Panel1.Controls.Add(Me.HomeTabChart)
        Application.DoEvents()
        Me.HomeTabChart.Series.Add(Me.HomeTabCurrentBGSeries)
        Me.HomeTabChart.Series.Add(Me.HomeTabMarkerSeries)
        Me.HomeTabChart.Series.Add(Me.HomeTabHighLimitSeries)
        Me.HomeTabChart.Series.Add(Me.HomeTabLowLimitSeries)
        Me.HomeTabChart.Legends.Add(defaultLegend)
        Me.HomeTabChart.Series(NameOf(HomeTabCurrentBGSeries)).EmptyPointStyle.BorderWidth = 4
        Me.HomeTabChart.Series(NameOf(HomeTabCurrentBGSeries)).EmptyPointStyle.Color = Color.Transparent
        Application.DoEvents()
    End Sub

    Private Sub InitializeTimeInRangeArea()
        Dim width1 As Integer = Me.SplitContainer3.Panel2.Width - 65
        Dim splitPanelMidpoint As Integer = Me.SplitContainer3.Panel2.Width \ 2
        For Each control1 As Control In Me.SplitContainer3.Panel2.Controls
            control1.Left = splitPanelMidpoint - (control1.Width \ 2)
        Next
        Me.HomeTabTimeInRangeChart = New Chart With {
            .Anchor = AnchorStyles.Top,
            .BackColor = Color.Transparent,
            .BackGradientStyle = GradientStyle.None,
            .BackSecondaryColor = Color.Transparent,
            .BorderlineColor = Color.Transparent,
            .BorderlineWidth = 0,
            .Size = New Size(width1, width1)
        }

        With Me.HomeTabTimeInRangeChart
            .BorderSkin.BackSecondaryColor = Color.Transparent
            .BorderSkin.SkinStyle = BorderSkinStyle.None
            Me.TimeInRangeChartArea = New ChartArea With {
                    .Name = NameOf(TimeInRangeChartArea),
                    .BackColor = Color.Black
                }
            .ChartAreas.Add(Me.TimeInRangeChartArea)
            .Location = New Point(Me.TimeInRangeChartLabel.FindHorizontalMidpoint - (.Width \ 2),
                                  CInt(Me.TimeInRangeChartLabel.FindVerticalMidpoint() - Math.Round(.Height / 2.5)))
            .Name = NameOf(HomeTabTimeInRangeChart)
            Me.HomeTabTimeInRangeSeries = New Series With {
                    .ChartArea = NameOf(TimeInRangeChartArea),
                    .ChartType = SeriesChartType.Doughnut,
                    .Name = NameOf(HomeTabTimeInRangeSeries)
                }
            .Series.Add(Me.HomeTabTimeInRangeSeries)
            .Series(NameOf(HomeTabTimeInRangeSeries))("DoughnutRadius") = "17"
        End With

        Me.SplitContainer3.Panel2.Controls.Add(Me.HomeTabTimeInRangeChart)
        Application.DoEvents()
    End Sub

#End Region

#Region "Update Data/Tables"

    Private Shared Sub GetLimitsList(ByRef limitsIndexList As Integer())

        Dim limitsIndex As Integer = 0
        For i As Integer = 0 To limitsIndexList.GetUpperBound(0)
            If limitsIndex + 1 < s_limits.Count AndAlso CInt(s_limits(limitsIndex + 1)("index")) < i Then
                limitsIndex += 1
            End If
            limitsIndexList(i) = limitsIndex
        Next
    End Sub

    Private Shared Sub InitializeWorkingPanel(ByRef layoutPanel1 As TableLayoutPanel, realPanel As TableLayoutPanel, Optional autoSize? As Boolean = Nothing)
        layoutPanel1 = realPanel
        layoutPanel1.Controls.Clear()
        layoutPanel1.RowCount = 1
        If autoSize IsNot Nothing Then
            layoutPanel1.AutoSize = CBool(autoSize)
        End If
    End Sub

    Private Sub FillOneRowOfTableLayoutPanel(layoutPanel As TableLayoutPanel, innerJson As List(Of Dictionary(Of String, String)), rowIndex As ItemIndexs, filterJsonData As Boolean, timeFormat As String)
        For Each jsonEntry As IndexClass(Of Dictionary(Of String, String)) In innerJson.WithIndex()
            Dim innerTableBlue As New TableLayoutPanel With {
                    .Anchor = AnchorStyles.Left Or AnchorStyles.Right,
                    .AutoScroll = False,
                    .AutoSize = True,
                    .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    .ColumnCount = 2,
                    .Dock = DockStyle.Fill,
                    .Margin = New Padding(0),
                    .Name = NameOf(innerTableBlue),
                    .Padding = New Padding(0)
                }
            layoutPanel.Controls.Add(innerTableBlue, column:=1, row:=jsonEntry.Index)
            Application.DoEvents()
            GetInnerTable(jsonEntry.Value, innerTableBlue, rowIndex, filterJsonData, timeFormat, Me.FormScale.Height <> 1)
            Application.DoEvents()
        Next
    End Sub

    Private Function IsRecentDataUpdated() As Boolean
        If _recentDatalast Is Nothing OrElse _RecentData Is Nothing Then
            Return False
        End If
        If _recentDataSameCount < 5 Then
            _recentDataSameCount += 1
            Dim i As Integer
            For i = 0 To _RecentData.Keys.Count - 1
                If _recentDatalast.Keys(i) <> "currentServerTime" AndAlso _recentDatalast.Values(i) <> _RecentData.Values(i) Then
                    _recentDataSameCount = 0
                    Return True
                End If
            Next
            Return False
        End If
        _recentDataSameCount = 0
        Return True
    End Function

    Private Sub UpdateActiveInsulinChart()
        If Not _initialized Then
            Exit Sub
        End If

        With Me.ActiveInsulinChart
            .Titles(NameOf(ActiveInsulinChartTitle)).Text = $"Running Active Insulin in Pink"
            .ChartAreas(NameOf(ActiveInsulinChartArea)).AxisX.Minimum = _sGs(0).OADate()
            .ChartAreas(NameOf(ActiveInsulinChartArea)).AxisX.Maximum = _sGs.Last.OADate()
            .Series(NameOf(ActiveInsulinSeries)).Points.Clear()
            .Series(NameOf(ActiveInsulinCurrentBGSeries)).Points.Clear()
            .Series(NameOf(ActiveInsulinMarkerSeries)).Points.Clear()
            .ChartAreas(NameOf(ActiveInsulinChartArea)).AxisX.MajorGrid.IntervalType = DateTimeIntervalType.Hours
            .ChartAreas(NameOf(ActiveInsulinChartArea)).AxisX.MajorGrid.IntervalOffsetType = DateTimeIntervalType.Hours
            .ChartAreas(NameOf(ActiveInsulinChartArea)).AxisX.MajorGrid.Interval = 1
            .ChartAreas(NameOf(ActiveInsulinChartArea)).AxisX.IntervalType = DateTimeIntervalType.Hours
            .ChartAreas(NameOf(ActiveInsulinChartArea)).AxisX.Interval = 2
        End With

        ' Order all markers by time
        Dim timeOrderedMarkers As New SortedDictionary(Of Double, Double)
        Dim sgOaDateTime As Double

        For Each marker As IndexClass(Of Dictionary(Of String, String)) In s_markers.WithIndex()
            sgOaDateTime = s_markers.SafeGetSgDateTime(marker.Index).RoundTimeDown(RoundTo.Minute).ToOADate
            Select Case marker.Value("type").ToString
                Case "INSULIN"
                    Dim bolusAmount As Double = marker.Value.GetDoubleValue("deliveredFastAmount")
                    If timeOrderedMarkers.ContainsKey(sgOaDateTime) Then
                        timeOrderedMarkers(sgOaDateTime) += bolusAmount
                    Else
                        timeOrderedMarkers.Add(sgOaDateTime, bolusAmount)
                    End If
                Case "AUTO_BASAL_DELIVERY"
                    Dim bolusAmount As Double = marker.Value.GetDoubleValue("bolusAmount")
                    If timeOrderedMarkers.ContainsKey(sgOaDateTime) Then
                        timeOrderedMarkers(sgOaDateTime) += bolusAmount
                    Else
                        timeOrderedMarkers.Add(sgOaDateTime, bolusAmount)
                    End If
                Case "AUTO_MODE_STATUS"
                Case "BG_READING"
                Case "CALIBRATION"
                Case "LOW_GLUCOSE_SUSPENDED"
                Case "MEAL"
                Case Else
                    Stop
            End Select
        Next

        ' set up table that holds active insulin for every 5 minutes
        Dim remainingInsulinList As New List(Of Insulin)
        Dim currentMarker As Integer = 0

        For i As Integer = 0 To 287
            Dim initialBolus As Double = 0
            Dim oaTime As Double = (_sGs(0).datetime + (s_fiveMinuteSpan * i)).RoundTimeDown(RoundTo.Minute).ToOADate()
            While currentMarker < timeOrderedMarkers.Count AndAlso timeOrderedMarkers.Keys(currentMarker) <= oaTime
                initialBolus += timeOrderedMarkers.Values(currentMarker)
                currentMarker += 1
            End While
            remainingInsulinList.Add(New Insulin(oaTime, initialBolus, _activeInsulinIncrements, Me.MenuOptionsUseAdvancedAITDecay.Checked))
        Next

        Me.ActiveInsulinChartArea.AxisY2.Maximum = Me.MarkerRow

        ' walk all markers, adjust active insulin and then add new marker
        Dim maxActiveInsulin As Double = 0
        For i As Integer = 0 To remainingInsulinList.Count - 1
            If i < _activeInsulinIncrements Then
                Me.ActiveInsulinChart.Series(NameOf(ActiveInsulinSeries)).Points.AddXY(remainingInsulinList(i).OaTime, Double.NaN)
                Me.ActiveInsulinChart.Series(NameOf(ActiveInsulinSeries)).Points.Last.IsEmpty = True
                If i > 0 Then
                    remainingInsulinList.Adjustlist(0, i)
                End If
                Continue For
            End If
            Dim startIndex As Integer = i - _activeInsulinIncrements + 1
            Dim sum As Double = remainingInsulinList.ConditionalSum(startIndex, _activeInsulinIncrements)
            maxActiveInsulin = Math.Max(sum, maxActiveInsulin)
            Dim x As Integer = Me.ActiveInsulinChart.Series(NameOf(ActiveInsulinSeries)).Points.AddXY(remainingInsulinList(i).OaTime, sum)
            remainingInsulinList.Adjustlist(startIndex, _activeInsulinIncrements)
            Application.DoEvents()
        Next
        Me.ActiveInsulinChartArea.AxisY.Maximum = Math.Ceiling(maxActiveInsulin) + 1
        maxActiveInsulin = Me.ActiveInsulinChartArea.AxisY.Maximum

        s_totalAutoCorrection = 0
        s_totalBasal = 0
        s_totalCarbs = 0
        s_totalDailyDose = 0
        s_totalManualBolus = 0

        For Each marker As IndexClass(Of Dictionary(Of String, String)) In s_markers.WithIndex()
            sgOaDateTime = s_markers.SafeGetSgDateTime(marker.Index).RoundTimeDown(RoundTo.Minute).ToOADate
            With Me.ActiveInsulinChart.Series(NameOf(ActiveInsulinMarkerSeries))
                Select Case marker.Value("type")
                    Case "INSULIN"
                        .Points.AddXY(sgOaDateTime, maxActiveInsulin)
                        Dim deliveredAmount As Single = marker.Value("deliveredFastAmount").ParseSingle
                        s_totalDailyDose += deliveredAmount
                        Select Case marker.Value("activationType")
                            Case "AUTOCORRECTION"
                                .Points.Last.ToolTip = $"Auto Correction: {deliveredAmount.ToString(CurrentUICulture)} U"
                                .Points.Last.Color = Color.MediumPurple
                                s_totalAutoCorrection += deliveredAmount
                            Case "RECOMMENDED", "UNDETERMINED"
                                .Points.Last.ToolTip = $"Bolus: {deliveredAmount.ToString(CurrentUICulture)} U"
                                .Points.Last.Color = Color.LightBlue
                                s_totalManualBolus += deliveredAmount
                            Case Else
                                Stop
                        End Select
                        .Points.Last.MarkerSize = 15
                        .Points.Last.MarkerStyle = MarkerStyle.Square

                    Case "AUTO_BASAL_DELIVERY"
                        Dim bolusAmount As Double = marker.Value.GetDoubleValue("bolusAmount")
                        .Points.AddXY(sgOaDateTime, maxActiveInsulin)
                        .Points.Last.ToolTip = $"Basal: {bolusAmount.RoundDouble(3).ToString(CurrentUICulture)} U"
                        .Points.Last.MarkerSize = 8
                        s_totalBasal += CSng(bolusAmount)
                        s_totalDailyDose += CSng(bolusAmount)
                    Case "MEAL"
                        s_totalCarbs += marker.Value.GetDoubleValue("amount")
                    Case "BG_READING"
                    Case "CALIBRATION"
                    Case "AUTO_MODE_STATUS"
                    Case "LOW_GLUCOSE_SUSPENDED"
                    Case Else
                        Stop
                End Select
            End With
        Next
        For Each sgListIndex As IndexClass(Of SgRecord) In _sGs.WithIndex()
            Dim bgValue As Single = sgListIndex.Value.sg

            Me.ActiveInsulinChart.Series(NameOf(ActiveInsulinCurrentBGSeries)).PlotOnePoint(
                sgListIndex.Value.OADate(),
                sgListIndex.Value.sg,
                Color.Black,
                Me.InsulinRow,
                _limitHigh,
                _limitLow
                )
        Next
        _initialized = True
        Application.DoEvents()
    End Sub

    Private Sub UpdateDataTables(isScaledForm As Boolean)
        If _RecentData Is Nothing Then
            Exit Sub
        End If
        _updating = True
        Me.Cursor = Cursors.WaitCursor
        Application.DoEvents()
        Me.TableLayoutPanelSummaryData.Controls.Clear()
        Dim rowCount As Integer = Me.TableLayoutPanelSummaryData.RowCount
        Dim newRowCount As Integer = _RecentData.Count - 9
        If rowCount < newRowCount Then
            Me.TableLayoutPanelSummaryData.RowCount = newRowCount
            For i As Integer = rowCount To newRowCount
                Me.TableLayoutPanelSummaryData.RowStyles.Add(New System.Windows.Forms.RowStyle(SizeType.Absolute, 22.0!))
            Next
        End If

        Dim currentRowIndex As Integer = 0
        Dim layoutPanel1 As TableLayoutPanel

        Dim firstName As String = ""
        For Each c As IndexClass(Of KeyValuePair(Of String, String)) In _RecentData.WithIndex()
            layoutPanel1 = Me.TableLayoutPanelSummaryData
            Dim row As KeyValuePair(Of String, String) = c.Value
            Dim rowIndex As ItemIndexs = CType([Enum].Parse(GetType(ItemIndexs), c.Value.Key), ItemIndexs)

            Select Case rowIndex
                Case ItemIndexs.medicalDeviceTimeAsString,
                     ItemIndexs.lastSensorTSAsString,
                     ItemIndexs.kind,
                     ItemIndexs.version,
                     ItemIndexs.currentServerTime,
                     ItemIndexs.lastConduitUpdateServerTime,
                     ItemIndexs.lastMedicalDeviceDataUpdateServerTime,
                     ItemIndexs.conduitSerialNumber,
                     ItemIndexs.conduitBatteryLevel,
                     ItemIndexs.conduitBatteryStatus,
                     ItemIndexs.conduitInRange,
                     ItemIndexs.conduitMedicalDeviceInRange,
                     ItemIndexs.conduitSensorInRange,
                     ItemIndexs.medicalDeviceFamily,
                     ItemIndexs.reservoirAmount,
                     ItemIndexs.calibStatus,
                     ItemIndexs.pumpCommunicationState,
                     ItemIndexs.calFreeSensor,
                     ItemIndexs.conduitInRange,
                     ItemIndexs.medicalDeviceSuspended,
                     ItemIndexs.lastSGTrend,
                     ItemIndexs.gstCommunicationState,
                     ItemIndexs.maxAutoBasalRate,
                     ItemIndexs.maxBolusAmount,
                     ItemIndexs.sgBelowLimit,
                     ItemIndexs.averageSGFloat,
                     ItemIndexs.timeToNextCalibrationRecommendedMinutes,
                     ItemIndexs.finalCalibration
                     ' String Handler

                Case ItemIndexs.pumpModelNumber
                    Me.ModelLabel.Text = row.Value
                     ' String Handler
                Case ItemIndexs.firstName
                    firstName = row.Value
                     ' String Handler
                Case ItemIndexs.lastName
                    Me.FullNameLabel.Text = $"{firstName} {row.Value}"
                     ' String Handler
                Case ItemIndexs.conduitInRange
                    s_conduitSensorInRange = CBool(row.Value)
                    ' String Handler
                Case ItemIndexs.sensorState
                    s_sensorState = row.Value
                    ' String Handler
                Case ItemIndexs.medicalDeviceSerialNumber
                    Me.SerialNumberLabel.Text = row.Value
                     ' String Handler
                Case ItemIndexs.reservoirLevelPercent
                    _reservoirLevelPercent = CInt(row.Value)
                     ' String Handler
                Case ItemIndexs.reservoirRemainingUnits
                    _reservoirRemainingUnits = row.Value.ParseDouble
                     ' String Handler
                Case ItemIndexs.medicalDeviceBatteryLevelPercent
                    _medicalDeviceBatteryLevelPercent = CInt(row.Value)
                     ' String Handler
                Case ItemIndexs.sensorDurationHours
                    _sensorDurationHours = CInt(row.Value)
                     ' String Handler
                Case ItemIndexs.timeToNextCalibHours
                    _timeToNextCalibHours = CUShort(row.Value)
                     ' String Handler
                Case ItemIndexs.bgUnits
                    Me.UpdateRegionalData(_RecentData)
                     ' String Handler
                Case ItemIndexs.timeFormat
                    _timeWithMinuteFormat = If(row.Value = "HR_12", TwelveHourTimeWithMinuteFormat, MilitaryTimeWithMinuteFormat)
                    _timeWithoutMinuteFormat = If(row.Value = "HR_12", TwelveHourTimeWithoutMinuteFormat, MilitaryTimeWithoutMinuteFormat)
                     ' String Handler
                Case ItemIndexs.systemStatusMessage
                    s_systemStatusMessage = row.Value
                     ' String Handler
                Case ItemIndexs.averageSG
                    s_averageSG = CInt(row.Value)
                     ' String Handler
                Case ItemIndexs.belowHypoLimit
                    s_belowHypoLimit = CInt(row.Value)
                     ' String Handler
                Case ItemIndexs.aboveHyperLimit
                    s_aboveHyperLimit = CInt(row.Value)
                     ' String Handler
                Case ItemIndexs.timeInRange
                    _timeInRange = CInt(row.Value)
                     ' String Handler
                Case ItemIndexs.gstBatteryLevel
                    s_gstBatteryLevel = CInt(row.Value)
                     ' String Handler
                Case ItemIndexs.sensorDurationMinutes
                    _sensorDurationMinutes = CInt(row.Value)
                    ' String Handler
                Case ItemIndexs.timeToNextCalibrationMinutes
                    _timeToNextCalibrationMinutes = CInt(row.Value)
                    ' String Handler
                Case ItemIndexs.clientTimeZoneName
                    s_clientTimeZoneName = row.Value
                     ' String Handler

                Case ItemIndexs.lastSensorTS,
                     ItemIndexs.lastConduitTime,
                     ItemIndexs.medicalDeviceTime,
                     ItemIndexs.sMedicalDeviceTime,
                     ItemIndexs.lastSensorTime,
                     ItemIndexs.sLastSensorTime,
                     ItemIndexs.lastConduitDateTime
                    ' Time Handler

                Case ItemIndexs.sgs
                    _sGs = LoadList(row.Value, True).ToSgList()
                    Me.SGsDataGridView.DataSource = _sGs
                    For Each column As DataGridViewTextBoxColumn In Me.SGsDataGridView.Columns
                        If _filterJsonData AndAlso s_alwaysFilter.Contains(column.Name) Then
                            Me.SGsDataGridView.Columns(column.Name).Visible = False
                        End If
                    Next
                    Me.ReadingsLabel.Text = $"{_sGs.Where(Function(entry As SgRecord) Not Double.IsNaN(entry.sg)).Count}/288"
                    Continue For

                Case ItemIndexs.lastSG
                    InitializeWorkingPanel(layoutPanel1, Me.TableLayoutPanelTop1)
                Case ItemIndexs.lastAlarm
                    InitializeWorkingPanel(layoutPanel1, Me.TableLayoutPanelTop2)
                Case ItemIndexs.activeInsulin
                    InitializeWorkingPanel(layoutPanel1, Me.TableLayoutPanelActiveInsulin)
                Case ItemIndexs.limits
                    InitializeWorkingPanel(layoutPanel1, Me.TableLayoutPanelLimits, True)
                Case ItemIndexs.markers
                    InitializeWorkingPanel(layoutPanel1, Me.TableLayoutPanelMarkers)
                Case ItemIndexs.notificationHistory
                    InitializeWorkingPanel(layoutPanel1, Me.TableLayoutPanelNotificationHistory)
                Case ItemIndexs.basal
                    InitializeWorkingPanel(layoutPanel1, Me.TableLayoutPanelBasal)
                Case ItemIndexs.therapyAlgorithmState
                    InitializeWorkingPanel(layoutPanel1, Me.TableLayoutPanelTherapyAlgorthm, True)
                Case ItemIndexs.pumpBannerState
                    InitializeWorkingPanel(layoutPanel1, Me.TableLayoutPanelBannerState, True)
                Case Else
                    Stop
                    Exit Select
            End Select

            Try
                Dim singleItem As Boolean
                Dim tableRelitiveRow As Integer
                If s_listOfSingleItems.Contains(rowIndex) Then
                    singleItem = True
                    tableRelitiveRow = 0
                Else
                    singleItem = False
                    tableRelitiveRow = currentRowIndex
                    currentRowIndex += 1
                End If
                layoutPanel1.RowStyles(tableRelitiveRow).SizeType = SizeType.AutoSize
                If (Not singleItem) OrElse
                    rowIndex = ItemIndexs.lastSG OrElse
                    rowIndex = ItemIndexs.lastAlarm OrElse
                    rowIndex = ItemIndexs.pumpBannerState Then
                    Dim columnHeaderLabel As New Label With {
                            .Text = $"{CInt(rowIndex)} {row.Key}",
                            .Anchor = AnchorStyles.Left Or AnchorStyles.Right,
                            .AutoSize = True
                        }
                    layoutPanel1.Controls.Add(columnHeaderLabel, 0, tableRelitiveRow)
                    Application.DoEvents()
                End If
                If row.Value?.StartsWith("[") Then
                    Dim innerListDictionary As List(Of Dictionary(Of String, String)) = LoadList(row.Value, False)
                    Select Case rowIndex
                        Case ItemIndexs.limits
                            s_limits = innerListDictionary
                        Case ItemIndexs.markers
                            s_markers = innerListDictionary
                        Case ItemIndexs.notificationHistory,
                             ItemIndexs.pumpBannerState
                            ' handled elsewhere
                        Case Else
                            Stop
                    End Select
                    If innerListDictionary.Count = 0 Then
                        Dim rowTextBox As New TextBox With {
                                .Anchor = AnchorStyles.Left Or AnchorStyles.Right,
                                .AutoSize = True,
                                .BackColor = Color.LightGray,
                                .BorderStyle = BorderStyle.FixedSingle,
                                .ReadOnly = True,
                                .Text = ""
                            }
                        layoutPanel1.Controls.Add(rowTextBox, 1, tableRelitiveRow)
                        Continue For
                    End If
                    layoutPanel1.Parent.Parent.UseWaitCursor = True
                    Application.DoEvents()
                    layoutPanel1.Invoke(Sub()
                                            Me.FillOneRowOfTableLayoutPanel(layoutPanel1,
                                                              innerListDictionary,
                                                              rowIndex,
                                                              _filterJsonData,
                                                              _timeWithMinuteFormat)
                                        End Sub)
                    layoutPanel1.Parent.Parent.UseWaitCursor = False
                    Application.DoEvents()
                    Continue For
                End If
                If Not (row.Value?.StartsWith("{")) Then
                    Dim resultDate As Date
                    Dim value As String = row.Value
                    If s_ListOfTimeItems.Contains(rowIndex) Then
                        If row.Value.TryParseDate(resultDate, "") Then
                            value = $"{value}   {resultDate.ToLongDateString} {resultDate.ToLongTimeString}"
                        End If
                    End If
                    Dim rowTextBox As New TextBox With {
                        .Anchor = AnchorStyles.Left Or AnchorStyles.Right,
                        .AutoSize = True,
                        .[ReadOnly] = True,
                        .Text = value
                    }
                    layoutPanel1.Controls.Add(rowTextBox,
                                              If(singleItem, 0, 1),
                                              tableRelitiveRow)

                    Continue For
                End If
                layoutPanel1.RowStyles(tableRelitiveRow).SizeType = SizeType.AutoSize
                Dim innerJsonDictionary As Dictionary(Of String, String) = Loads(row.Value)
                Dim docStyle As DockStyle = DockStyle.Fill
                Select Case rowIndex
                    Case ItemIndexs.lastSG
                        s_lastSG = innerJsonDictionary
                    Case ItemIndexs.activeInsulin
                        s_activeInsulin = innerJsonDictionary
                    Case ItemIndexs.therapyAlgorithmState,
                         ItemIndexs.basal
                        docStyle = DockStyle.Top
                    Case ItemIndexs.lastAlarm,
                         ItemIndexs.notificationHistory
                        ' handled elsewhere
                    Case Else
                        Stop
                End Select
                Dim innerTableBlue As New TableLayoutPanel With {
                        .Anchor = AnchorStyles.Left Or AnchorStyles.Right,
                        .AutoScroll = True,
                        .AutoSize = True,
                        .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        .ColumnCount = 2,
                        .Dock = docStyle,
                        .Margin = New Padding(0),
                        .Name = NameOf(innerTableBlue),
                        .Padding = New Padding(0)
                    }
                layoutPanel1.Controls.Add(innerTableBlue,
                                          If(singleItem AndAlso Not (rowIndex = ItemIndexs.lastSG OrElse rowIndex = ItemIndexs.lastAlarm), 0, 1),
                                          tableRelitiveRow)
                If rowIndex = ItemIndexs.notificationHistory Then
                    innerTableBlue.AutoScroll = False
                End If
                GetInnerTable(innerJsonDictionary, innerTableBlue, rowIndex, _filterJsonData, _timeWithMinuteFormat, isScaledForm)
            Catch ex As Exception
                Stop
                'Throw
            End Try
        Next
        If _RecentData.Count > ItemIndexs.finalCalibration + 1 Then
            Stop
        End If
        _initialized = True
        _updating = False
        Me.Cursor = Cursors.Default
    End Sub

    Private Sub UpdateDosingAndCarbs()
        Dim totalPercent As String
        If s_totalDailyDose = 0 Then
            totalPercent = "???"
        Else
            totalPercent = $"{CInt(s_totalBasal / s_totalDailyDose * 100)}"
        End If
        Me.BasalLabel.Text = $"Basal {s_totalBasal.RoundSingle(1)} U | {totalPercent}%"

        Me.DailyDoseLabel.Text = $"Daily Dose {s_totalDailyDose.RoundSingle(1)} U"

        If s_totalAutoCorrection > 0 Then
            If s_totalDailyDose > 0 Then
                totalPercent = CInt(s_totalAutoCorrection / s_totalDailyDose * 100).ToString
            End If
            Me.AutoCorrectionLabel.Text = $"Auto Correction {s_totalAutoCorrection.RoundSingle(1)} U | {totalPercent}%"
            Me.AutoCorrectionLabel.Visible = True
            Dim totalBolus As Single = s_totalManualBolus + s_totalAutoCorrection
            If s_totalDailyDose > 0 Then
                totalPercent = CInt(s_totalManualBolus / s_totalDailyDose * 100).ToString
            End If
            Me.ManualBolusLabel.Text = $"Manual Bolus {totalBolus.RoundSingle(1)} U | {totalPercent}%"
        Else
            Me.AutoCorrectionLabel.Visible = False
            If s_totalDailyDose > 0 Then
                totalPercent = CInt(s_totalManualBolus / s_totalDailyDose * 100).ToString
            End If
            Me.ManualBolusLabel.Text = $"Bolus {s_totalManualBolus.RoundSingle(1)} U | {totalPercent}%"
        End If
        Me.Last24CarbsValueLabel.Text = s_totalCarbs.ToString
    End Sub

    Private Sub UpdateRegionalData(localRecentData As Dictionary(Of String, String))
        Dim bgUnits As String = ""
        If localRecentData.TryGetValue(ItemIndexs.bgUnits.ToString, bgUnits) Then
            Me.BgUnitsString = GetLocalizedUnits(bgUnits)
            If Me.BgUnitsString = "mg/dl" Then
                _markerRow = 400
                _limitHigh = 180
                _limitLow = 70
                _insulinRow = 50
            Else
                _markerRow = (400 / 18).RoundSingle(1)
                _limitHigh = (180 / 18).RoundSingle(1)
                _limitLow = (70 / 18).RoundSingle(1)
                _insulinRow = (50 / 18).RoundSingle(1)
            End If
        End If

        If localRecentData.TryGetValue(ItemIndexs.clientTimeZoneName.ToString, s_clientTimeZoneName) Then
            Dim cleanTimeZoneName As String = s_clientTimeZoneName.
                                                    Replace("Daylight", "Standard").
                                                    Replace("Summer", "Standard")
            s_clientTimeZone = s_timeZoneList.Where(Function(t As TimeZoneInfo)
                                                        Return t.Id = cleanTimeZoneName
                                                    End Function).FirstOrDefault
            If s_clientTimeZone Is Nothing Then
                If MsgBox($"Your pump timezone '{s_clientTimeZoneName}' is not recognized. If you continue '{TimeZoneInfo.Local.Id}' will be issue. Cancel will exit program. Please open an issue and provide the name '{s_clientTimeZoneName}'.", MsgBoxStyle.OkCancel) = MsgBoxResult.Yes Then
                    s_clientTimeZone = TimeZoneInfo.Local
                Else
                    End
                End If
            End If
        End If
        Dim internaltimeFormat As String = Nothing
        If localRecentData.TryGetValue(ItemIndexs.timeFormat.ToString, internaltimeFormat) Then
            _timeWithMinuteFormat = If(internaltimeFormat = "HR_12", TwelveHourTimeWithMinuteFormat, MilitaryTimeWithMinuteFormat)
            _timeWithoutMinuteFormat = If(internaltimeFormat = "HR_12", TwelveHourTimeWithoutMinuteFormat, MilitaryTimeWithoutMinuteFormat)
        End If
        Me.AboveHighLimitMessageLabel.Text = $"Above {_limitHigh} {Me.BgUnitsString}"
        Me.BelowLowLimitMessageLabel.Text = $"Below {_limitLow} {Me.BgUnitsString}"

    End Sub

    Friend Sub UpdateAllTabPages()
        If _RecentData Is Nothing OrElse _updating Then
            Exit Sub
        End If
        _updating = True
        Me.UpdateDataTables(Me.FormScale.Height <> 1 OrElse Me.FormScale.Width <> 1)
        Me.UpdateActiveInsulinChart()
        Me.UpdateActiveInsulin()
        Me.UpdateAutoModeShield()
        Me.UpdateCalibrationTimeRemaining()
        Me.UpdateInsulinLevel()
        Me.UpdatePumpBattery()
        Me.UpdateRemainingInsulin()
        Me.UpdateSensorLife()
        Me.UpdateTimeInRange()
        Me.UpdateTransmitterBatttery()

        Me.UpdateZHomeTabSerieses()
        Me.UpdateDosingAndCarbs()
        _recentDatalast = _RecentData
        _initialized = True
        _updating = False
        Application.DoEvents()
    End Sub

#Region "Home Page Update Utilities"

    Private Sub UpdateActiveInsulin()
        Dim activeInsulinStr As String = $"{s_activeInsulin("amount"):N3}"
        Me.ActiveInsulinValue.Text = $"{activeInsulinStr} U"
        _bgMiniDisplay.ActiveInsulinTextBox.Text = $"Active Insulin {activeInsulinStr}U"
    End Sub

    Private Sub UpdateAutoModeShield()
        Me.SensorMessage.Location = New Point(Me.ShieldPictureBox.Left + (Me.ShieldPictureBox.Width \ 2) - (Me.SensorMessage.Width \ 2), Me.SensorMessage.Top)
        If s_lastSG("sg") <> "0" Then
            Me.CurrentBG.Visible = True
            Me.CurrentBG.Location = New Point((Me.ShieldPictureBox.Width \ 2) - (Me.CurrentBG.Width \ 2), Me.ShieldPictureBox.Height \ 4)
            Me.CurrentBG.Parent = Me.ShieldPictureBox
            Me.CurrentBG.Text = s_lastSG("sg")
            Me.NotifyIcon1.Text = $"Last SG {s_lastSG("sg")} {Me.BgUnitsString}"
            _bgMiniDisplay.SetCurrentBGString(s_lastSG("sg"))
            Me.SensorMessage.Visible = False
            Me.ShieldPictureBox.Image = My.Resources.Shield
            Me.ShieldUnitsLabel.Visible = True
            Me.ShieldUnitsLabel.BackColor = Color.Transparent
            Me.ShieldUnitsLabel.Parent = Me.ShieldPictureBox
            Me.ShieldUnitsLabel.Left = (Me.ShieldPictureBox.Width \ 2) - (Me.ShieldUnitsLabel.Width \ 2)
            Me.ShieldUnitsLabel.Text = Me.BgUnitsString
            Me.ShieldUnitsLabel.Visible = True
        Else
            _bgMiniDisplay.SetCurrentBGString("---")
            Me.CurrentBG.Visible = False
            Me.ShieldPictureBox.Image = My.Resources.Shield_Disabled
            Me.SensorMessage.Visible = True
            Me.SensorMessage.Parent = Me.ShieldPictureBox
            Me.SensorMessage.Left = 0
            Me.SensorMessage.BackColor = Color.Transparent
            Dim message As String = ""
            If s_messages.TryGetValue(s_sensorState, message) Then
                message = s_sensorState.ToTitle
            Else
                MsgBox($"{s_sensorState} is unknown sensor message", MsgBoxStyle.OkOnly, $"Form 1 line:{New StackFrame(0, True).GetFileLineNumber()}")
            End If
            Me.SensorMessage.Text = message
            Me.ShieldUnitsLabel.Visible = False
            Me.SensorMessage.Visible = True
        End If
        If _bgMiniDisplay.Visible Then
            _bgMiniDisplay.BGTextBox.SelectionLength = 0
        End If
        Application.DoEvents()
    End Sub

    Private Sub UpdateCalibrationTimeRemaining()
        If _timeToNextCalibHours = Byte.MaxValue Then
            Me.CalibrationDueImage.Image = My.Resources.CalibrationUnavailable
        ElseIf _timeToNextCalibHours < 1 Then
            Me.CalibrationDueImage.Image = If(s_systemStatusMessage = "WAIT_TO_CALIBRATE" OrElse s_sensorState = "WARM_UP",
            My.Resources.CalibrationNotReady,
            My.Resources.CalibrationDotRed.DrawCenteredArc(_timeToNextCalibHours, _timeToNextCalibHours / 12))
        Else
            Me.CalibrationDueImage.Image = My.Resources.CalibrationDot.DrawCenteredArc(_timeToNextCalibHours, _timeToNextCalibHours / 12)
        End If

        Application.DoEvents()
    End Sub

    Private Sub UpdateInsulinLevel()
        Select Case _reservoirLevelPercent
            Case > 85
                Me.InsulinLevelPictureBox.Image = Me.ImageList1.Images(7)
            Case > 71
                Me.InsulinLevelPictureBox.Image = Me.ImageList1.Images(6)
            Case > 57
                Me.InsulinLevelPictureBox.Image = Me.ImageList1.Images(5)
            Case > 43
                Me.InsulinLevelPictureBox.Image = Me.ImageList1.Images(4)
            Case > 29
                Me.InsulinLevelPictureBox.Image = Me.ImageList1.Images(3)
            Case > 15
                Me.InsulinLevelPictureBox.Image = Me.ImageList1.Images(2)
            Case > 1
                Me.InsulinLevelPictureBox.Image = Me.ImageList1.Images(1)
            Case Else
                Me.InsulinLevelPictureBox.Image = Me.ImageList1.Images(0)
        End Select
        Application.DoEvents()
    End Sub

    Private Sub UpdatePumpBattery()
        If Not s_conduitSensorInRange Then
            Me.PumpBatteryPictureBox.Image = My.Resources.PumpBatteryUnknown
            Me.PumpBatteryRemainingLabel.Text = $"Unknown"
            Exit Sub
        End If

        Select Case _medicalDeviceBatteryLevelPercent
            Case > 66
                Me.PumpBatteryPictureBox.Image = My.Resources.PumpBatteryFull
                Me.PumpBatteryRemainingLabel.Text = $"High"
            Case >= 45
                Me.PumpBatteryPictureBox.Image = My.Resources.PumpBatteryMedium
                Me.PumpBatteryRemainingLabel.Text = $"Medium"
            Case > 25
                Me.PumpBatteryPictureBox.Image = My.Resources.PumpBatteryLow
                Me.PumpBatteryRemainingLabel.Text = $"Low"
            Case = 0
                Me.PumpBatteryPictureBox.Image = My.Resources.PumpBatteryCritical
                Me.PumpBatteryRemainingLabel.Text = $"Critical"
        End Select
    End Sub

    Private Sub UpdateRemainingInsulin()
        Me.RemainingInsulinUnits.Text = $"{_reservoirRemainingUnits:N1} U"
    End Sub

    Private Sub UpdateSensorLife()
        If _sensorDurationHours = 255 Then
            Me.SensorDaysLeftLabel.Text = $"???"
            Me.SensorTimeLeftPictureBox.Image = My.Resources.SensorExpirationUnknown
            Me.SensorTimeLeftLabel.Text = ""
        ElseIf _sensorDurationHours >= 24 Then
            Me.SensorDaysLeftLabel.Text = CStr(Math.Ceiling(_sensorDurationHours / 24))
            Me.SensorTimeLeftPictureBox.Image = My.Resources.SensorLifeOK
            Me.SensorTimeLeftLabel.Text = $"{Me.SensorDaysLeftLabel.Text} Days"
        Else
            If _sensorDurationHours = 0 Then
                If _sensorDurationMinutes = 0 Then
                    Me.SensorDaysLeftLabel.Text = ""
                    Me.SensorTimeLeftPictureBox.Image = My.Resources.SensorExpired
                    Me.SensorTimeLeftLabel.Text = $"Expired"
                Else
                    Me.SensorDaysLeftLabel.Text = $"1"
                    Me.SensorTimeLeftPictureBox.Image = My.Resources.SensorLifeNotOK
                    Me.SensorTimeLeftLabel.Text = $"{_sensorDurationMinutes} Minutes"
                End If
            Else
                Me.SensorDaysLeftLabel.Text = $"1"
                Me.SensorTimeLeftPictureBox.Image = My.Resources.SensorLifeNotOK
                Me.SensorTimeLeftLabel.Text = $"{_sensorDurationHours + 1} Hours"
            End If
        End If
        Me.SensorDaysLeftLabel.Visible = True
    End Sub

    Private Sub UpdateTimeInRange()
        With Me.HomeTabTimeInRangeChart
            With .Series(NameOf(HomeTabTimeInRangeSeries)).Points
                .Clear()
                .AddXY($"{s_aboveHyperLimit}% Above {_limitHigh} {Me.BgUnitsString}", s_aboveHyperLimit / 100)
                .Last().Color = Color.Orange
                .Last().BorderColor = Color.Black
                .Last().BorderWidth = 2
                .AddXY($"{s_belowHypoLimit}% Below {_limitLow} {Me.BgUnitsString}", s_belowHypoLimit / 100)
                .Last().Color = Color.Red
                .Last().BorderColor = Color.Black
                .Last().BorderWidth = 2
                .AddXY($"{_timeInRange}% In Range", _timeInRange / 100)
                .Last().Color = Color.LawnGreen
                .Last().BorderColor = Color.Black
                .Last().BorderWidth = 2
            End With
            .Series(NameOf(HomeTabTimeInRangeSeries))("PieLabelStyle") = "Disabled"
            .Series(NameOf(HomeTabTimeInRangeSeries))("PieStartAngle") = "270"
        End With

        Me.TimeInRangeChartLabel.Text = _timeInRange.ToString
        Me.TimeInRangeValueLabel.Text = $"{_timeInRange} %"
        Me.AboveHighLimitValueLabel.Text = $"{s_aboveHyperLimit} %"
        Me.BelowLowLimitValueLabel.Text = $"{s_belowHypoLimit} %"
        Me.AverageSGMessageLabel.Text = $"Average SG in {Me.BgUnitsString}"
        Me.AverageSGValueLabel.Text = If(Me.BgUnitsString = "mg/dl", s_averageSG.ToString, s_averageSG.RoundDouble(1).ToString())

    End Sub

    Private Sub UpdateTransmitterBatttery()
        Me.TransmatterBatterPercentLabel.Text = $"{s_gstBatteryLevel}%"
        If s_conduitSensorInRange Then
            Select Case s_gstBatteryLevel
                Case 100
                    Me.TransmitterBatteryPictureBox.Image = My.Resources.TransmitterBatteryFull
                Case > 50
                    Me.TransmitterBatteryPictureBox.Image = My.Resources.TransmitterBatteryOK
                Case > 20
                    Me.TransmitterBatteryPictureBox.Image = My.Resources.TransmitterBatteryMedium
                Case > 0
                    Me.TransmitterBatteryPictureBox.Image = My.Resources.TransmitterBatteryLow
            End Select
        Else
            Me.TransmitterBatteryPictureBox.Image = My.Resources.TransmitterBatteryUnknown
            Me.TransmatterBatterPercentLabel.Text = $"???"
        End If

    End Sub

    Private Sub UpdateZHomeTabSerieses()
        Me.HomeTabChart.Series(NameOf(HomeTabCurrentBGSeries)).Points.Clear()
        Me.HomeTabChart.Series(NameOf(HomeTabMarkerSeries)).Points.Clear()
        Me.HomeTabChart.Series(NameOf(HomeTabHighLimitSeries)).Points.Clear()
        Me.HomeTabChart.Series(NameOf(HomeTabLowLimitSeries)).Points.Clear()
        _markerInsulinDictionary.Clear()
        _markerMealDictionary.Clear()
        For Each markerListIndex As IndexClass(Of Dictionary(Of String, String)) In s_markers.WithIndex()
            Dim markerDateTime As Date = s_markers.SafeGetSgDateTime(markerListIndex.Index)
            Dim markerOaDateTime As Double = markerDateTime.ToOADate()
            Dim bgValueString As String = ""
            Dim bgValue As Single
            If markerListIndex.Value.TryGetValue("value", bgValueString) Then
                Single.TryParse(bgValueString, NumberStyles.Number, CurrentDataCulture, bgValue)
            End If
            With Me.HomeTabChart.Series(NameOf(HomeTabMarkerSeries)).Points
                Select Case markerListIndex.Value("type")
                    Case "BG_READING"
                        Single.TryParse(markerListIndex.Value("value"), NumberStyles.Number, CurrentDataCulture, bgValue)
                        .AddXY(markerOaDateTime, bgValue)
                        .Last.BorderColor = Color.Gainsboro
                        .Last.Color = Color.Transparent
                        .Last.MarkerBorderWidth = 2
                        .Last.MarkerSize = 10
                        .Last.ToolTip = $"Blood Glucose: Not used For calibration: {bgValue.ToString(CurrentUICulture)} {Me.BgUnitsString}"
                    Case "CALIBRATION"
                        .AddXY(markerOaDateTime, bgValue)
                        .Last.BorderColor = Color.Red
                        .Last.Color = Color.Transparent
                        .Last.MarkerBorderWidth = 2
                        .Last.MarkerSize = 8
                        .Last.ToolTip = $"Blood Glucose: Calibration {If(CBool(markerListIndex.Value("calibrationSuccess")), "accepted", "not accepted")}: {markerListIndex.Value("value")} {Me.BgUnitsString}"
                    Case "INSULIN"
                        _markerInsulinDictionary.Add(markerOaDateTime, CInt(Me.MarkerRow))
                        .AddXY(markerOaDateTime, Me.MarkerRow)
                        Dim result As Single
                        Single.TryParse(markerListIndex.Value("deliveredFastAmount"), NumberStyles.Number, CurrentDataCulture, result)
                        Select Case markerListIndex.Value("activationType")
                            Case "AUTOCORRECTION"
                                .Last.Color = Color.FromArgb(60, Color.MediumPurple)
                                .Last.ToolTip = $"Auto Correction: {result.ToString(CurrentUICulture)} U"
                            Case "RECOMMENDED", "UNDETERMINED"
                                .Last.Color = Color.FromArgb(30, Color.LightBlue)
                                .Last.ToolTip = $"Bolus: {result.ToString(CurrentUICulture)} U"
                            Case Else
                                Stop
                        End Select
                        .Last.MarkerBorderWidth = 0
                        .Last.MarkerSize = 30
                        .Last.MarkerStyle = MarkerStyle.Square
                    Case "MEAL"
                        _markerMealDictionary.Add(markerOaDateTime, Me.InsulinRow)
                        .AddXY(markerOaDateTime, Me.InsulinRow)
                        .Last.Color = Color.FromArgb(30, Color.Yellow)
                        .Last.MarkerBorderWidth = 0
                        .Last.MarkerSize = 30
                        .Last.MarkerStyle = MarkerStyle.Square
                        Dim result As Single
                        Single.TryParse(markerListIndex.Value("amount"), NumberStyles.Number, CurrentDataCulture, result)
                        .Last.ToolTip = $"Meal:{result.ToString(CurrentUICulture)} grams"
                    Case "AUTO_BASAL_DELIVERY"
                        .AddXY(markerOaDateTime, Me.MarkerRow)
                        Dim bolusAmount As String = markerListIndex.Value("bolusAmount")
                        .Last.MarkerBorderColor = Color.Black
                        .Last.ToolTip = $"Basal:{bolusAmount.RoundDouble(3).ToString(CurrentUICulture)} U"
                    Case "TIME_CHANGE"
                        ' need to handle
                    Case "AUTO_MODE_STATUS", "LOW_GLUCOSE_SUSPENDED"
                        'Stop
                    Case Else
                        Stop
                End Select
            End With
        Next
        Dim limitsIndexList(_sGs.Count - 1) As Integer
        GetLimitsList(limitsIndexList)
        For Each sgListIndex As IndexClass(Of SgRecord) In _sGs.WithIndex()
            Dim sgOaDateTime As Double = sgListIndex.Value.OADate()
            PlotOnePoint(Me.HomeTabChart.Series(NameOf(HomeTabCurrentBGSeries)), sgOaDateTime, sgListIndex.Value.sg, Color.White, Me.InsulinRow, _limitHigh, _limitLow)
            Dim limitsLowValue As Integer = CInt(s_limits(limitsIndexList(sgListIndex.Index))("lowLimit"))
            Dim limitsHighValue As Integer = CInt(s_limits(limitsIndexList(sgListIndex.Index))("highLimit"))
            If limitsHighValue <> 0 Then
                Me.HomeTabChart.Series(NameOf(HomeTabHighLimitSeries)).Points.AddXY(sgOaDateTime, limitsHighValue)
            End If
            If limitsLowValue <> 0 Then
                Me.HomeTabChart.Series(NameOf(HomeTabLowLimitSeries)).Points.AddXY(sgOaDateTime, limitsLowValue)
            End If
        Next
    End Sub

#End Region

#End Region

    Private Sub CleanUpNotificationIcon()
        Me.NotifyIcon1.Visible = False
        Me.NotifyIcon1.Icon.Dispose()
        Me.NotifyIcon1.Icon = Nothing
        Me.NotifyIcon1.Visible = False
        Me.NotifyIcon1.Dispose()
        Application.DoEvents()
        End
    End Sub

    Private Function DoOptionalLoginAndUpdateData(UpdateAllTabs As Boolean) As Boolean
        Me.ServerUpdateTimer.Stop()
        Debug.Print($"Me.ServerUpdateTimer stopped at {Now}")
        If Me.MenuOptionsUseTestData.Checked Then
            Me.MenuView.Visible = False
            Me.Text = $"{SavedTitle} Using Test Data"
            CurrentDateCulture = New CultureInfo("en-US")
            _RecentData = Loads(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SampleUserData.json")))
        ElseIf Me.MenuOptionsUseLastSavedData.Checked Then
            Me.MenuView.Visible = False
            Me.Text = $"{SavedTitle} Using Last Saved Data"
            CurrentDateCulture = LastDownloadWithPath.ExtractCultureFromFileName(RepoDownloadName)
            _RecentData = Loads(File.ReadAllText(LastDownloadWithPath))
        Else
            Me.Text = SavedTitle
            _loginDialog.ShowDialog()
            _client = _loginDialog.Client
            If _client Is Nothing OrElse Not _client.LoggedIn Then
                Return False
            End If
            _RecentData = _client.GetRecentData()
            Me.MenuView.Visible = True
            Me.ServerUpdateTimer.Interval = CType(New TimeSpan(0, minutes:=1, 0).TotalMilliseconds, Integer)
            Me.ServerUpdateTimer.Start()
            Debug.Print($"Me.ServerUpdateTimer Started at {Now}")
            Me.LoginStatus.Text = "OK"
        End If
        If Not _initialized Then
            Me.FinishInitialization()
        End If
        If UpdateAllTabs Then
            Me.UpdateAllTabPages()
        End If
        Return True
    End Function

    Private Sub Fix(sp As SplitContainer)
        ' Scale factor depends on orientation
        Dim sc As Single = If(sp.Orientation = Orientation.Vertical, Me.FormScale.Width, Me.FormScale.Height)
        If sp.FixedPanel = FixedPanel.Panel1 Then
            sp.SplitterDistance = CInt(Math.Truncate(Math.Round(sp.SplitterDistance * sc)))
        ElseIf sp.FixedPanel = Global.System.Windows.Forms.FixedPanel.Panel2 Then
            Dim cs As Integer = If(sp.Orientation = Orientation.Vertical, sp.Panel2.ClientSize.Width, sp.Panel2.ClientSize.Height)
            Dim newcs As Integer = CInt(Math.Truncate(cs * sc))
            sp.SplitterDistance -= newcs - cs
        End If
    End Sub

    ' Recursively search for SplitContainer controls
    Private Sub Fix(c As Control)
        For Each child As Control In c.Controls
            If TypeOf child Is SplitContainer Then
                Dim sp As SplitContainer = CType(child, SplitContainer)
                Me.Fix(sp)
                Me.Fix(sp.Panel1)
                Me.Fix(sp.Panel2)
            Else
                Me.Fix(child)
            End If
        Next child
    End Sub

    Friend Sub FinishInitialization()
        If _initialized Then
            Exit Sub
        End If
        _homePageChartRelitivePosition = RectangleF.Empty
        _updating = True
        Me.Cursor = Cursors.WaitCursor
        Application.DoEvents()
        Me.UpdateRegionalData(_RecentData)
        _updating = False
        Me.Cursor = Cursors.Default
        Application.DoEvents()

        Me.InitializeHomePageChart()
        Me.InitializeActiveInsulinTabChart()
        Me.InitializeTimeInRangeArea()
        Me.SGsDataGridView.AutoGenerateColumns = True
        Me.SGsDataGridView.ColumnHeadersDefaultCellStyle = New DataGridViewCellStyle With {
            .Alignment = DataGridViewContentAlignment.MiddleCenter
            }

        _initialized = True
    End Sub

End Class
