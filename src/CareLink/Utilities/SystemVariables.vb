﻿' Licensed to the .NET Foundation under one or more agreements.
' The .NET Foundation licenses this file to you under the MIT license.
' See the LICENSE file in the project root for more information.

Friend Module SystemVariables
    Friend s_useLocalTimeZone As Boolean
    Public s_allUserSettingsData As New CareLinkUserDataList

    Friend Property CurrentUser As CurrentUserRecord

    Friend Property GraphColorDictionary As New Dictionary(Of String, KnownColor) From {
                        {"Active Insulin", KnownColor.Lime},
                        {"Auto Correction", KnownColor.Aqua},
                        {"Basal Series", KnownColor.HotPink},
                        {"High Limit", KnownColor.Yellow},
                        {"Low Limit", KnownColor.Red},
                        {"Min Basal", KnownColor.LightYellow},
                        {"SG Series", KnownColor.White},
                        {"SG Target", KnownColor.Green},
                        {"Time Change", KnownColor.White}
                    }

    Friend Property MaxBasalPerDose As Single

    Friend Property nativeMmolL As Boolean = False

    Friend Property TreatmentInsulinRow As Single

    Friend Function GetInsulinYValue() As Single
        Dim maxYScaled As Single = s_listOfSGs.Max(Of Single)(Function(sgR As SgRecord) sgR.sg) + 2
        If Single.IsNaN(maxYScaled) Then
            Return If(nativeMmolL, 330 / MmolLUnitsDivisor, 330)
        Else
            If nativeMmolL Then
                If s_listOfSGs.Count = 0 OrElse maxYScaled > (330 / MmolLUnitsDivisor) Then
                    Return 342 / MmolLUnitsDivisor
                End If
                Return Math.Max(maxYScaled, 260 / MmolLUnitsDivisor)
            Else
                If s_listOfSGs.Count = 0 OrElse maxYScaled > 330 Then
                    Return 342
                End If
                Return Math.Max(maxYScaled, 260)
            End If
        End If
    End Function

    Friend Function GetTIR() As UInteger
        If s_timeInRange > 0 Then
            Return CUInt(s_timeInRange)
        End If
        Return CUInt(100 - (s_aboveHyperLimit + s_belowHypoLimit))
    End Function

    Friend Function GetYMaxValue(asMmolL As Boolean) As Single
        Return If(asMmolL, CSng(22.2), 400)
    End Function

    Friend Function GetYMinValue(asMmolL As Boolean) As Single
        Return If(asMmolL, CSng(2.8), 50)
    End Function

    Friend Function TirHighLimit(asMmolL As Boolean) As Single
        Return If(asMmolL, 10, 180)
    End Function

    Friend Function TirLowLimit(asMmolL As Boolean) As Single
        Return If(asMmolL, CSng(3.9), 70)
    End Function

End Module
