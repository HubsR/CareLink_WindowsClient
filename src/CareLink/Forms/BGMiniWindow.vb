﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Imports System.ComponentModel
Imports System.Media

Public Class BGMiniWindow
    Private ReadOnly _form1 As Form1
    Private _alarmPlayedHigh As Boolean
    Private _alarmPlayedLow As Boolean
    Private _currentBGValue As Single = Double.NaN
    Private _lastBGValue As Single
    Private _normalizedBG As Single

    Public Sub New()
        MyBase.New
        Me.InitializeComponent()
    End Sub

    Public Sub New(form1 As Form1)
        MyBase.New
        Me.InitializeComponent()
        _form1 = form1
    End Sub

    Private Shared Function GetLastUpdateMessage() As String
        If s_lastMedicalDeviceDataUpdateServerEpoch = 0 Then
            Return $"{s_firstName}'s Last Update Unknown"
        End If
        Return $"{s_firstName}'s Updated {CInt((PumpNow() - s_lastMedicalDeviceDataUpdateServerEpoch.Epoch2DateTime).TotalMinutes)} minutes ago"
    End Function

    Private Sub ActiveInsulinTextBox_GotFocus(sender As Object, e As EventArgs) Handles ActiveInsulinTextBox.GotFocus
        Me.HiddenTextBox.Focus()
    End Sub

    Private Sub BGMiniWindow_Closing(sender As Object, e As CancelEventArgs) Handles MyBase.Closing
        _form1.Visible = True
    End Sub

    Private Sub BGMiniWindow_GotFocus(sender As Object, e As EventArgs) Handles MyBase.GotFocus
        Me.HiddenTextBox.Focus()
    End Sub

    Private Sub BGMiniWindow_KeyDown(sender As Object, e As KeyEventArgs) Handles MyBase.KeyDown
        If e.Modifiers = Keys.Alt AndAlso e.KeyCode = Keys.W Then
            _form1.Visible = True
            Me.Hide()
        End If
    End Sub

    Private Sub BGTextBox_GotFocus(sender As Object, e As EventArgs) Handles BGTextBox.GotFocus
        Me.HiddenTextBox.Focus()
    End Sub

    Private Sub BGTextBox_TextChanged(sender As Object, e As EventArgs) Handles BGTextBox.TextChanged
        Me.Text = GetLastUpdateMessage()
        If Me.BGTextBox.Text.Length = 0 OrElse Me.BGTextBox.Text = "---" OrElse Me.BGTextBox.Text = "9999" Then
            _currentBGValue = Double.NaN
            Me.DeltaTextBox.Text = ""
        Else
            If Double.IsNaN(_currentBGValue) OrElse _currentBGValue = 0 OrElse Double.IsNaN(_lastBGValue) OrElse _lastBGValue = 0 Then
                Me.DeltaTextBox.Text = ""
            Else
                Dim delta As Double = _currentBGValue - _lastBGValue
                Me.DeltaTextBox.Text = delta.ToString(If(nativeMmolL, $"+0.00;-#.00", "+0;-#0"), CurrentUICulture)
                Select Case delta
                    Case Is = 0
                        Me.DeltaTextBox.Text = ""
                    Case Is > 0
                        Me.DeltaTextBox.ForeColor = Color.Blue
                    Case Is < 0
                        Me.DeltaTextBox.ForeColor = Color.Orange
                End Select
            End If
            Select Case _normalizedBG
                Case = 0
                    Me.BGTextBox.ForeColor = Color.Black
                Case <= 70
                    Me.BGTextBox.ForeColor = Color.Red
                    If Not _alarmPlayedLow Then
                        Me.playSoundFromResource("Low Alarm")
                        _alarmPlayedLow = True
                        _alarmPlayedHigh = False
                    End If
                Case <= 180
                    Me.BGTextBox.ForeColor = Color.Green
                    _alarmPlayedLow = False
                    _alarmPlayedHigh = False
                Case Else
                    Me.BGTextBox.ForeColor = Color.Yellow
                    If Not _alarmPlayedHigh Then
                        Me.playSoundFromResource("High Alarm")
                        _alarmPlayedLow = False
                        _alarmPlayedHigh = True
                    End If
            End Select
        End If

    End Sub

    Private Sub ChkTopMost_CheckedChanged(sender As Object, e As EventArgs) Handles ChkTopMost.CheckedChanged
        If Me.ChkTopMost.Checked Then
            Me.TopMost = True
        ElseIf Not Me.ChkTopMost.Checked Then
            Me.TopMost = False
        End If
    End Sub

    Private Sub CloseButton_Click(sender As Object, e As EventArgs) Handles CloseButton.Click
        _form1.Visible = True
        Me.Hide()
    End Sub

    Private Sub playSoundFromResource(SoundName As String)
        Using player As New SoundPlayer(My.Resources.ResourceManager.GetStream(SoundName, CurrentUICulture))
            player.Play()
        End Using
    End Sub

    Public Sub SetCurrentBGString(Value As String)
        If String.IsNullOrEmpty(Value) Then
            Value = "---"
        End If

        _lastBGValue = _currentBGValue
        _currentBGValue = Value.ParseSingle(2)
        If Not Double.IsNaN(_currentBGValue) Then
            _normalizedBG = _currentBGValue
            If nativeMmolL Then
                _normalizedBG *= MmolLUnitsDivisor
            End If
            Me.BGTextBox.ForeColor = SystemColors.ControlText
            Me.BGTextBox.Text = If(nativeMmolL, Value.ParseSingle(1).ToString(CurrentUICulture), CInt(_currentBGValue).ToString)
        Else
            Me.BGTextBox.ForeColor = Color.Red
            Me.BGTextBox.Text = Value
        End If
        Me.Text = GetLastUpdateMessage()
    End Sub

End Class
