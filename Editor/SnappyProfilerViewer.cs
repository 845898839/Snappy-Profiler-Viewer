﻿/*
* This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
* distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public class SnappyProfilerViewer : EditorWindow {
    // The fixed width for the columns such as "Calls" and "Self Time", which are numerical
    private const float numericalDataColumnWidth = 75f;

    private static readonly int scrubberHash = "Scrubber".GetHashCode();
    private static GUIStyle labelStyle, rightAlignedLabel, columnHeadersStyleLeft, columnHeadersStyleCentered, evenRowStyle, oddRowStyle;

    private List<ProfilerRowInfo> cachedProfilerProperties = new List<ProfilerRowInfo>();
    private int selectedFrame = 0;
    private int SelectedFrame {
        get {
            return selectedFrame;
        }
        set {
            if(value == selectedFrame) return;
            selectedFrame = Mathf.Clamp(value, ProfilerDriver.firstFrameIndex, ProfilerDriver.lastFrameIndex);
            UpdateProperties();
        }
    }
    private int selectedProperty;

    private Vector2 scrollPosition;
    
    private ProfilerColumn columnToSort = ProfilerColumn.TotalTime;
    private ProfilerColumn ColumnToSort {
        set {
            if(value == columnToSort) return;
            columnToSort = value;
            UpdateProperties();
        }
    }

    private const float cellHeight = 16f;
    private Rect columnHeadersRect;
    private float headerLeftOffset = 0f;
    private float cellTopOffset;
    private float cellLeftOffset;
    private float functionNameCellWidth;

    private Texture2D frameTimeGraphTexture;
    
    private int previousFirstFrameIndex = -1;
    private int previousLastFrameIndex = -1;

    private int lastWidth = -1;

    [MenuItem("Jesse Stiller/Snappy Profiler Viewer...")]
    private static void CreateAndShow() {
        GetWindow<SnappyProfilerViewer>(false, "Snappy", true);
    }
    
    private void OnGUI() {
        /**
        * GUIStyles initialization
        */
        if(labelStyle == null) labelStyle = new GUIStyle("OL Label");
        if(rightAlignedLabel == null) {
            rightAlignedLabel = new GUIStyle("OL Label");
            rightAlignedLabel.alignment = TextAnchor.MiddleRight;
            rightAlignedLabel.padding = new RectOffset(0, 5, 0, 0);
        }
        if(columnHeadersStyleLeft == null) {
            columnHeadersStyleLeft = new GUIStyle("OL title");
            columnHeadersStyleLeft.alignment = TextAnchor.MiddleLeft;
        }
        if(columnHeadersStyleCentered == null) {
            columnHeadersStyleCentered = new GUIStyle("OL title");
            columnHeadersStyleCentered.alignment = TextAnchor.MiddleCenter;
        }
        if(evenRowStyle == null) evenRowStyle = new GUIStyle("OL EntryBackEven");
        if(oddRowStyle == null) oddRowStyle = new GUIStyle("OL EntryBackOdd");

        Event current = Event.current;

        if(current.type == EventType.Repaint && lastWidth != Screen.width) {
            if(lastWidth == -1) {
                lastWidth = Screen.width;
            } else {
                lastWidth = Screen.width;
                UpdateFrameScrubberGraph(Screen.width);
            }
        }
        
        if(current.type != EventType.Layout && (ProfilerDriver.firstFrameIndex != previousFirstFrameIndex || ProfilerDriver.lastFrameIndex != previousLastFrameIndex)) {
            previousFirstFrameIndex = ProfilerDriver.firstFrameIndex;
            previousLastFrameIndex = ProfilerDriver.lastFrameIndex;
            UpdateFrameScrubberGraph(Screen.width);
        }

        /**
        * Frame scrubber and frame time colour graph
        */
        Rect frameTimeGraphTextureRect = new Rect(0f, 0f, Screen.width, 30f);
        int scrubberControlID = GUIUtility.GetControlID(scrubberHash, FocusType.Keyboard, frameTimeGraphTextureRect);
        int numberOfFrames = ProfilerDriver.lastFrameIndex - ProfilerDriver.firstFrameIndex;
        
        if(current.type == EventType.Repaint && frameTimeGraphTexture != null) {
            EditorGUI.DrawPreviewTexture(frameTimeGraphTextureRect, frameTimeGraphTexture);
        }

        switch(current.GetTypeForControl(scrubberControlID)) {
            case EventType.MouseDown:
                if(frameTimeGraphTextureRect.Contains(current.mousePosition)) {
                    GUIUtility.hotControl = scrubberControlID;
                } else {
                    GUIUtility.hotControl = 0;
                }
                break;
        }

        if(GUIUtility.hotControl == scrubberControlID) {
            switch(current.type) {
                case EventType.MouseDown:
                case EventType.MouseDrag:
                case EventType.MouseUp:
                    SelectedFrame = Mathf.RoundToInt((current.mousePosition.x / Screen.width) * numberOfFrames) + ProfilerDriver.firstFrameIndex;
                    break;
            }

            if(current.isKey && current.type != EventType.KeyUp) {
                if(current.keyCode == KeyCode.LeftArrow) SelectedFrame = Mathf.Max(SelectedFrame - 1, 0);
                if(current.keyCode == KeyCode.RightArrow) SelectedFrame = Mathf.Min(SelectedFrame + 1, ProfilerDriver.lastFrameIndex);
            }
        }

        // Frame cursor/scrubber
        float scrubberLeftOffset = (((float)SelectedFrame - ProfilerDriver.firstFrameIndex) / numberOfFrames) * frameTimeGraphTextureRect.width - 2.5f;

        EditorGUI.DrawRect(new Rect(scrubberLeftOffset, frameTimeGraphTextureRect.y, 5f, frameTimeGraphTextureRect.height), Color.grey);
        
        if(ProfilerDriver.firstFrameIndex == -1) {
            EditorGUILayout.HelpBox("There is no profiling data to view. Begin profiling, and then stop profiling when there's frames you wish to view.", MessageType.Warning);
            return;
        }

        if(cachedProfilerProperties == null || cachedProfilerProperties.Count == 0) {
            SelectedFrame = ProfilerDriver.firstFrameIndex;
            UpdateProperties();
        }

        // Frame statistics
        Rect frameStatisticsRowRect = new Rect(0f, frameTimeGraphTextureRect.yMax, Screen.width, 20f);
        GUI.Label(new Rect(0f, frameStatisticsRowRect.y, 30f, 20f), ProfilerDriver.firstFrameIndex.ToString());

        GUIStyle rightAlignedStandardLabel = new GUIStyle(GUI.skin.label);
        rightAlignedStandardLabel.alignment = TextAnchor.MiddleRight;
        GUI.Label(new Rect(Screen.width - 30f, frameStatisticsRowRect.y, 30f, 20f), ProfilerDriver.lastFrameIndex.ToString(), rightAlignedStandardLabel);

        GUIStyle centeredLabel = new GUIStyle(GUI.skin.label);
        centeredLabel.alignment = TextAnchor.MiddleCenter;
        GUI.Label(new Rect((((float)SelectedFrame - ProfilerDriver.firstFrameIndex) / numberOfFrames) * Screen.width - 15f, 
            frameStatisticsRowRect.y, 30f, 20f), SelectedFrame.ToString(), centeredLabel);

        /**
        * Draw the column headers
        */
        columnHeadersRect = new Rect(0f, frameStatisticsRowRect.yMax, Screen.width - 15f, columnHeadersStyleCentered.fixedHeight);
        float functionNameHeaderWidth = columnHeadersRect.width - numericalDataColumnWidth * 6f;
        float columnHeadersLeft = columnHeadersRect.x;

        headerLeftOffset = 0f;
        DrawColumnHeader("Function Name", ProfilerColumn.FunctionName, functionNameHeaderWidth, columnHeadersStyleLeft);
        DrawColumnHeader("Total %", ProfilerColumn.TotalPercent, numericalDataColumnWidth, columnHeadersStyleCentered);
        DrawColumnHeader("Self %", ProfilerColumn.SelfPercent, numericalDataColumnWidth, columnHeadersStyleCentered);
        DrawColumnHeader("Calls", ProfilerColumn.Calls, numericalDataColumnWidth, columnHeadersStyleCentered);
        DrawColumnHeader("GC Alloc", ProfilerColumn.GCMemory, numericalDataColumnWidth, columnHeadersStyleCentered);
        DrawColumnHeader("Total ms", ProfilerColumn.TotalTime, numericalDataColumnWidth, columnHeadersStyleCentered);
        DrawColumnHeader("Self ms", ProfilerColumn.SelfTime, numericalDataColumnWidth, columnHeadersStyleCentered);
        
        Rect scrollViewRect = new Rect(0f, Mathf.Ceil(columnHeadersRect.yMax + 1f), Screen.width, Screen.height - columnHeadersRect.yMax - 24);

        Rect viewRect = new Rect(scrollViewRect.x, scrollViewRect.y, scrollViewRect.width - 15f, cellHeight * cachedProfilerProperties.Count);
        scrollPosition = GUI.BeginScrollView(scrollViewRect, scrollPosition, viewRect);

        int firstVisibleProperty = Mathf.Max(Mathf.CeilToInt(scrollPosition.y / cellHeight) - 1, 0);
        int lastVisibleProperty = Mathf.Min(firstVisibleProperty + Mathf.CeilToInt(scrollViewRect.height / cellHeight + 1), cachedProfilerProperties.Count);

        bool selectRows = false;

        // Check if the first visible property is a child of a selected property, to make sure it is also selected
        if(selectedProperty < firstVisibleProperty) {
            selectRows = true;
            for(int i = selectedProperty + 1; i < firstVisibleProperty; i++) {
                if(cachedProfilerProperties[i].depth <= cachedProfilerProperties[selectedProperty].depth) {
                    selectRows = false;
                    break;
                }
            }
        }

        /**
        * Draw the data
        */
        for(int i = firstVisibleProperty; i < lastVisibleProperty; i++) {
            cellTopOffset = i * cellHeight + columnHeadersRect.y + cellHeight + 2f;

            // Background
            Rect cellRect = new Rect(0f, cellTopOffset, scrollViewRect.width, cellHeight);
            
            if(current.type == EventType.MouseDown) {
                if(cellRect.Contains(current.mousePosition)) {
                    selectedProperty = i;
                    Repaint();
                }
            }
            
            if(Event.current.type != EventType.Repaint) continue;

            if(i == selectedProperty) {
                selectRows = true;
            } else if(selectRows) {
                if(cachedProfilerProperties[i].depth <= cachedProfilerProperties[selectedProperty].depth) {
                    selectRows = false;
                }
            }

            GUIStyle backgroundStyle = (i % 2 == 0 ? evenRowStyle : oddRowStyle);
            backgroundStyle.Draw(cellRect, GUIContent.none, false, false, selectRows, false);

            cellLeftOffset = (cachedProfilerProperties[i].depth - 1) * 15f + 4f;
            functionNameCellWidth = functionNameHeaderWidth - (cachedProfilerProperties[i].depth - 1) * 15f;

            DrawDataCell(cachedProfilerProperties[i].functionName, functionNameCellWidth, labelStyle, selectRows);
            DrawDataCell(cachedProfilerProperties[i].totalPercent, numericalDataColumnWidth, rightAlignedLabel, selectRows);
            DrawDataCell(cachedProfilerProperties[i].selfPercent, numericalDataColumnWidth, rightAlignedLabel, selectRows);
            DrawDataCell(cachedProfilerProperties[i].calls, numericalDataColumnWidth, rightAlignedLabel, selectRows);
            DrawDataCell(cachedProfilerProperties[i].gcMemory, numericalDataColumnWidth, rightAlignedLabel, selectRows);
            DrawDataCell(cachedProfilerProperties[i].totalTime, numericalDataColumnWidth, rightAlignedLabel, selectRows);
            DrawDataCell(cachedProfilerProperties[i].selfTime, numericalDataColumnWidth, rightAlignedLabel, selectRows);
        }
        GUI.EndScrollView();
    }
    
    private void UpdateFrameScrubberGraph(int width) {
        if(cachedProfilerProperties == null) UpdateProperties();

        int numberOfFrames = ProfilerDriver.lastFrameIndex - ProfilerDriver.firstFrameIndex;

        if(frameTimeGraphTexture == null) {
            frameTimeGraphTexture = new Texture2D(width, 1);
        } else {
            frameTimeGraphTexture.Resize(width, 1);
        }

        float frameTimeTotal = 0f;
        float[] frameTimes = new float[numberOfFrames];
        float minFrameTime = float.MaxValue;
        float maxFrameTime = float.MinValue;
        
        // Make sure ProfilerProperties are never created anew in an inner loop as it will cause Unity to crash while disposing of them.
        ProfilerProperty property = new ProfilerProperty();

        // Calculate the total frame time and the minimum and maximum frame times.
        for(int f = 0; f < numberOfFrames; f++) {
            property.SetRoot(f + ProfilerDriver.firstFrameIndex, ProfilerColumn.DontSort, ProfilerViewType.RawHierarchy);

            frameTimes[f] = float.Parse(property.frameTime);
            frameTimeTotal += frameTimes[f];

            if(frameTimes[f] < minFrameTime) minFrameTime = frameTimes[f];
            if(frameTimes[f] > maxFrameTime) maxFrameTime = frameTimes[f];
        }

        float frameSize = width / (numberOfFrames - 1f);
        float[] bloom = new float[width];

        // Create an intense neon look based on the maximum frame time out of the current samples.
        for(int f = 0; f < numberOfFrames; f++) {
            float frameTimeNormalized = (frameTimes[f] - minFrameTime) / (maxFrameTime - minFrameTime);
            int leftOffset = Mathf.RoundToInt(((float)f / numberOfFrames) * width);
            int blurSize = Mathf.CeilToInt(Mathf.Pow(frameTimeNormalized * frameSize, 1.4f));
            blurSize = Mathf.Max(blurSize, 4);
            
            for(int b = -blurSize; b < blurSize; b++) {
                if(leftOffset + b < 0 || leftOffset + b >= width) continue;

                float blurOffset = (blurSize - Mathf.Abs((float)b)) / blurSize;
                bloom[leftOffset + b] += Mathf.Pow(blurOffset, 3f) * frameTimeNormalized; 
            }
        }
        
        for(int i = 0; i < width; i++) {
            float brightness;
            if(bloom[i] < 0.8f) {
                brightness = bloom[i] / 0.8f;
            } else {
                brightness = 1f;
            }

            frameTimeGraphTexture.SetPixel(i, 0, Color.Lerp(Color.red, Color.yellow, bloom[i]) * brightness);
        }

        frameTimeGraphTexture.Apply();
    }

    private void DrawDataCell(string data, float width, GUIStyle style, bool selected) {
        style.Draw(new Rect(cellLeftOffset, cellTopOffset, width, cellHeight), data, false, false, false, selected);
        cellLeftOffset += width;
    }

    private void DrawColumnHeader(string label, ProfilerColumn column, float width, GUIStyle style) {
        bool toggleResult = GUI.Toggle(new Rect(headerLeftOffset, columnHeadersRect.y, width, columnHeadersRect.height),
            columnToSort == column, label, style);

        if(toggleResult == true) ColumnToSort = column;

        headerLeftOffset += width;
    }

    private void UpdateProperties() {
        ProfilerProperty profilerProperty = new ProfilerProperty();
        profilerProperty.SetRoot(SelectedFrame, columnToSort, ProfilerViewType.Hierarchy);
        profilerProperty.onlyShowGPUSamples = false;

        cachedProfilerProperties.Clear();

        while(profilerProperty.Next(true)) {
            cachedProfilerProperties.Add(new ProfilerRowInfo(profilerProperty));
        }

        Repaint();
    }

    private struct ProfilerRowInfo {
        internal int depth;
        internal string propertyPath, functionName, totalPercent, selfPercent, calls, gcMemory, totalTime, selfTime;

        public ProfilerRowInfo(ProfilerProperty profilerProperty) {
            propertyPath = profilerProperty.propertyPath;
            depth = profilerProperty.depth;
            functionName = profilerProperty.GetColumn(ProfilerColumn.FunctionName);
            totalPercent = profilerProperty.GetColumn(ProfilerColumn.TotalPercent);
            selfPercent = profilerProperty.GetColumn(ProfilerColumn.SelfPercent);
            calls = profilerProperty.GetColumn(ProfilerColumn.Calls);
            gcMemory = profilerProperty.GetColumn(ProfilerColumn.GCMemory);
            totalTime = profilerProperty.GetColumn(ProfilerColumn.TotalTime);
            selfTime = profilerProperty.GetColumn(ProfilerColumn.SelfTime);
        }
    }
}
