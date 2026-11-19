using CascadeControl.Graphs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CascadeControlTests.Graphs
{
    /// <summary>
    /// 圖相關測試共用的測試資料與比對工具。
    /// </summary>
    internal static class TestGraphs
    {
        /// <summary>
        /// GraphDesign 3.7 節的小圖：A 指向 x、m、n，m 與 n 都指向 1、2，另有孤立頂點 Z。
        /// </summary>
        public static MenuGraph Small()
        {
            var graph = new MenuGraph();
            graph.ExpandAndAddEdges("A", "x|m|n");
            graph.ExpandAndAddEdges("m|n", "1|2");
            graph.GetOrAddVertex("Z");
            return graph;
        }

        /// <summary>
        /// GraphDesign 第 7 節的範例，以 Compact 寫法表示（46 列、346 個頂點、610 條邊、無環）。
        /// </summary>
        public static readonly string[] SampleLines =
        {
            "Main,System|Motor|IO|Vision|RfTest|Network|RobotArm|Process",
            "System,Info|Status|Config|Power|Clock|Watchdog|License|About",
            "Info|Status,Version|BuildDate|Uptime|CpuLoad|MemoryUsage|DiskSpace|TempSensor|FanSpeed",
            "Config,LoadDefault|SaveProfile|ExportCfg|ImportCfg|SleepMode|WakeSource|Reboot|Shutdown",
            "Motor,AxisX|AxisY|AxisZ|AxisU|AxisV|AxisW|Spindle|Conveyor",
            "AxisX|AxisY,Home|Jog|MoveAbs|MoveRel|Speed|Torque|Limit|Alarm",
            "Jog|Speed,Step1um|Step10um|Step100um|Step1mm|Step10mm|Continuous|Fast|Slow",
            "IO,DigitalIn|DigitalOut|AnalogIn|AnalogOut|SafetyIO|RelayMatrix|PwmChannel|Counter",
            "DigitalIn|AnalogIn,Bank0|Bank1|Bank2|Bank3|Bank4|Bank5|Bank6|Bank7",
            "Bank0|Bank1,Ch0|Ch1|Ch2|Ch3|Ch4|Ch5|Ch6|Ch7",
            "Ch0|Ch1,ReadPin|SetHigh|SetLow|Toggle|Invert|Debounce|Latch|ClearLatch",
            "Vision,CamTop|CamBottom|CamLeft|CamRight|CamAlign1|CamAlign2|CamInspect|CamBarcode",
            "CamTop|CamBottom,Capture|Exposure|Lighting|Calibration|Pattern|Measure|Filter|Overlay",
            "Pattern|Measure,Roi1|Roi2|Roi3|Roi4|Roi5|Roi6|RoiFull",
            "Roi1|Roi2,FindEdge|FindCircle|FindLine|FindBlob|MatchModel|ScoreCheck|AngleComp",
            "FindEdge|MatchModel,RunOnce|RunLoop|SaveTemplate|LoadTemplate|SetThreshold|SetPolarity|ShowResult|ExportImg",
            "RfTest,Pretest|TxTest|RxTest|Calib|BurnIn|Sweep|Harmonic|FinalCheck",
            "TxTest|RxTest,Band2G|Band5G|Band6G|Ble|Zigbee|Uwb|Gnss|Lte",
            "Band2G|Band5G,WifiCh1|WifiCh6|WifiCh11|WifiCh36|WifiCh40|WifiCh44|WifiCh48",
            "WifiCh1|WifiCh36,Rate6M|Rate12M|Rate24M|Rate54M|Mcs0|Mcs7|Mcs9",
            "Mcs0|Mcs7,ChainA|ChainB|ChainC|ChainD|Mimo2x2|Mimo4x4|Beamform",
            "ChainA|ChainB,MeasurePower|MeasureEvm|MeasureMask|MeasureFreqErr|MeasureLeakage|SetAtten|SaveLog|AbortMeasure",
            "Network,Ethernet|Wifi|Modbus|EtherCat|CanBus|Serial|Grpc|Mqtt",
            "Ethernet|EtherCat,Port1|Port2|Port3|Port4|Port5|Port6|Port7",
            "Port1|Port2,Link|Packet|Session|Route|Firewall|Qos|Diag",
            "Packet|Diag,TxQueue|RxQueue|DropCnt|CrcErr|Latency|Jitter|Throughput",
            "Latency|Jitter,Window1s|Window5s|Window10s|Window30s|Window1m|Window5m|Window15m",
            "Window1s|Window5s,SampleMin|SampleMax|SampleAvg|Percentile50|Percentile90|Percentile99|Histogram",
            "Percentile99|Histogram,StartCapture|StopCapture|ResetStat|DumpCsv|PlotCurve|SetAlarm|CompareBase|SendReport",
            "RobotArm,ArmLeft|ArmRight|Gripper|Turntable|Loader|Unloader|Aligner|Buffer",
            "ArmLeft|ArmRight,Joint1|Joint2|Joint3|Joint4|Joint5|Joint6|Flange",
            "Joint1|Joint2,Servo|Reducer|Brake|Encoder|Temp|Current|Vibration",
            "Servo|Encoder,PidLoop|NotchFilter|FeedForward|FrictionComp|GravityComp|Backlash|SoftLimit",
            "PidLoop|NotchFilter,GainKp|GainKi|GainKd|VelKp|VelKi|TorqueFilter|CutoffFreq",
            "GainKp|GainKi,ProfileLow|ProfileMed|ProfileHigh|ProfileAuto|ProfileCustom1|ProfileCustom2|ProfileSafe",
            "ProfileAuto|ProfileCustom1,StepResp|SineSweep|BodePlot|ImpulseTest|LoadVerify|OvershootChk|SettleTime",
            "StepResp|BodePlot,Execute|Abort|SaveParam|RestoreParam|ExportData|ComparePrev|ApplyToAll|ResetFault",
            "Process,WaferLoad|PreAlign|ChamberA|ChamberB|EtchStep|Deposition|CleanCycle|WaferUnload",
            "ChamberA|ChamberB,Stage1|Stage2|Stage3|Stage4|Stage5|Stage6|Stage7",
            "Stage1|Stage2,Vacuum|GasFlow|PlasmaRf|Heater|ChuckEsc|ThrottleValve|EndPoint",
            "Vacuum|GasFlow,LineAr|LineO2|LineN2|LineCf4|LineChf3|LineSf6|LineHe",
            "LineAr|LineO2,MfcCtrl|ValvePre|ValveMain|PurgeCycle|LeakCheck|PressureReg|FlowVerify",
            "MfcCtrl|PurgeCycle,RampPhase1|RampPhase2|HoldPhase|StepDown1|StepDown2|Evacuate|Stabilize",
            "HoldPhase|Stabilize,ZoneInner|ZoneMiddle|ZoneOuter|ZoneEdge|ZoneCenter|ZoneSideA|ZoneSideB",
            "ZoneInner|ZoneOuter,TrimMinus3|TrimMinus2|TrimMinus1|Nominal|TrimPlus1|TrimPlus2|TrimPlus3",
            "Nominal|TrimPlus1,RunStep|HoldStep|SkipStep|AbortStep|ReadSensor|LogTrace|VerifySpec|CommitRecipe",
        };

        /// <summary>
        /// 範例的資料列（每列兩欄）。
        /// </summary>
        public static string[][] SampleRows() => SampleLines.Select(line => line.Split(',')).ToArray();

        /// <summary>
        /// 範例的 CSV 文字，含 From,To 表頭。
        /// </summary>
        public static string SampleText() => "From,To" + Environment.NewLine + string.Join(Environment.NewLine, SampleLines) + Environment.NewLine;

        /// <summary>
        /// 由範例建立的圖。
        /// </summary>
        public static MenuGraph Sample() => MenuGraph.FromRows(SampleRows(), out _);

        /// <summary>
        /// 產生隨機的圖：名稱池很小，刻意讓匯流、自我迴圈、環、孤立頂點、後繼序列相同的頂點都常出現。
        /// </summary>
        public static MenuGraph Random(int seed)
        {
            var rng = new Random(seed);
            var graph = new MenuGraph();
            var pool = Enumerable.Range(0, rng.Next(2, 12)).Select(i => "v" + i).ToArray();
            var templates = Enumerable.Range(0, 3)
                .Select(_ => Enumerable.Range(0, rng.Next(1, 4)).Select(__ => pool[rng.Next(pool.Length)]).Distinct().ToArray())
                .ToList();

            var steps = rng.Next(1, 25);
            for (var s = 0; s < steps; s++)
            {
                var from = pool[rng.Next(pool.Length)];
                switch (rng.Next(4))
                {
                    case 0:
                        graph.GetOrAddVertex(from);
                        break;

                    case 1:
                        foreach (var to in templates[rng.Next(templates.Count)]) graph.AddEdge(from, to);
                        break;

                    default:
                        graph.AddEdge(from, pool[rng.Next(pool.Length)]);
                        break;
                }
            }

            return graph;
        }

        /// <summary>
        /// 讀回保證涵蓋的內容：頂點集合、每個頂點的後繼序列、來源頂點的相對順序。
        /// 頂點整體順序與前驅順序不在保證內，所以不放進來。
        /// </summary>
        public static string Fingerprint(MenuGraph graph)
        {
            var lines = graph.Vertices
                .OrderBy(vertex => vertex.Name, StringComparer.Ordinal)
                .Select(vertex => vertex.Name + ": " + string.Join(",", vertex.Successors.Select(successor => successor.Name)))
                .ToList();
            lines.Add("sources: " + string.Join(",", graph.Sources.Select(vertex => vertex.Name)));
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// 一列展開後的邊數：From 格的名稱數乘以 To 格的名稱數；To 為空時為 0。
        /// </summary>
        public static long EdgesOf(string[] row)
        {
            if (row.Length < 2 || row[1].Length == 0) return 0;
            return (long)row[0].Split('|').Length * row[1].Split('|').Length;
        }

        /// <summary>
        /// 依名稱取得頂點，找不到時丟例外，讓測試寫起來短一點。
        /// </summary>
        public static MenuVertex V(this MenuGraph graph, string name)
        {
            if (!graph.TryGetVertex(name, out MenuVertex vertex)) throw new KeyNotFoundException(name);
            return vertex;
        }

        /// <summary>
        /// 頂點名稱以逗號串起來，方便比對。
        /// </summary>
        public static string Names(IEnumerable<MenuVertex> vertices) => string.Join(",", vertices.Select(vertex => vertex.Name));
    }
}