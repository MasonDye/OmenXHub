using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OmenSuperHub.Services
{
    public class XtuService : IDisposable
    {
        private const string ServiceName = "XTU3SERVICE";
        private const string PipeName = "XTU3SERVICE";
        private const int TimeoutMs = 5000;

        private NamedPipeClientStream _pipe;
        private bool _isConnected;

        public bool IsConnected => _isConnected && _pipe?.IsConnected == true;

        public async Task<bool> InitializeAsync()
        {
            try
            {
                if (!IsServiceRunningWmi())
                {
                    Logger.Info("XTU3SERVICE 未运行,尝试启动...");
                    if (!StartServiceWmi())
                    {
                        Logger.Error("无法启动 XTU3SERVICE");
                        return false;
                    }
                }

                _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await _pipe.ConnectAsync(TimeoutMs);
                _isConnected = true;

                Logger.Info("✅ XTU3SERVICE 连接成功");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"XTU3SERVICE 连接失败: {ex.Message}");
                _isConnected = false;
                return false;
            }
        }

        public async Task<OverclockingInfo> GetOverclockingInfoAsync()
        {
            var result = new OverclockingInfo();

            try
            {
                var initResult = await SendRpcAsync<string>("InitIntelSDKService()", 769);
                Logger.Info($"XTU SDK 初始化: {initResult}");

                result.IsOverclockSupported = await SendRpcAsync<bool>("XTUIsOverclockSupported()", 263);
                result.IsSystemUnlocked = await SendRpcAsync<bool>("XTUIsSystemUnlocked()", 264);
                result.IsTurboBoostEnabled = await SendRpcAsync<bool>("XTUIsTurboBoostTechnologyEnabled()", 265);
                result.IsCoreOcEnabled = await SendRpcAsync<bool>("XTUIsProcessorIACoreOCEnabled()", 266);
                result.IsClrOcEnabled = await SendRpcAsync<bool>("XTUIsProcessorClrOCEnabled()", 267);
                result.ServiceVersion = await SendRpcAsync<string>("XTUGetServiceVersion()", 275);
                result.PhysicalCoreCount = await SendRpcAsync<uint>("XTUGetPhysicalCpuCoreCount()", 277);
                result.EfficientCoreCount = await SendRpcAsync<uint>("XTUGetPhysicalCpuSmallCoreCount()", 278);

                Logger.Info($"超频支持: {result.IsOverclockSupported}, 解锁: {result.IsSystemUnlocked}");
            }
            catch (Exception ex)
            {
                Logger.Error($"获取超频信息失败: {ex.Message}");
            }

            return result;
        }

        public async Task<List<TuningControl>> GetAllControlsAsync()
        {
            var controls = new List<TuningControl>();

            try
            {
                for (uint id = 0x1F0; id <= 0x200; id++)
                {
                    try
                    {
                        var control = await GetControlAsync(id);
                        if (control != null && control.Enabled)
                        {
                            controls.Add(control);
                            Logger.Verbose($"控制项 0x{id:X}: {control.Name}, 当前值: {control.ActiveValue}");
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"枚举控制项失败: {ex.Message}");
            }

            return controls;
        }

        public async Task<TuningControl> GetControlAsync(uint controlId)
        {
            try
            {
                var request = new { controlId = controlId };
                var json = JsonConvert.SerializeObject(request);
                var response = await SendRpcAsync<string>(json, 259);

                var result = JsonConvert.DeserializeObject<TuningControlJson>(response);
                if (result?.Controls?.Count > 0)
                {
                    var c = result.Controls[0];
                    return new TuningControl
                    {
                        Id = c.Id,
                        Name = GetControlName(c.Id),
                        DefaultValue = c.DefaultValue,
                        ActiveValue = c.ActiveValue,
                        MinValue = c.MinPossibleValue,
                        MaxValue = c.MaxPossibleValue,
                        Enabled = c.Enabled,
                        ReadOnly = c.ReadOnly,
                        RequiresReboot = c.RequiresReboot,
                        SupportedValues = result.SupportedValues
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Verbose($"获取控制项 0x{controlId:X} 失败: {ex.Message}");
            }

            return null;
        }

        public async Task<bool> SetCoreRatioAsync(Dictionary<uint, decimal> coreRatios)
        {
            try
            {
                var proposals = new List<TuningProposal>();
                foreach (var kvp in coreRatios)
                {
                    proposals.Add(new TuningProposal { Id = kvp.Key, Value = kvp.Value });
                }

                var request = new { array = proposals };
                var json = JsonConvert.SerializeObject(request);
                var response = await SendRpcAsync<string>(json, 270);

                var result = JsonConvert.DeserializeObject<TuningProposalResultJson>(response);
                var success = result?.Results?.TrueForAll(r => r.Result) == true;

                if (success)
                {
                    await ApplyChangesAsync(rebootRequired: false);
                    Logger.Info($"✅ 设置核心倍频成功");
                }

                return success;
            }
            catch (Exception ex)
            {
                Logger.Error($"设置核心倍频失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SetVoltageOffsetAsync(Dictionary<uint, decimal> voltageOffsets)
        {
            try
            {
                var proposals = new List<TuningProposal>();
                foreach (var kvp in voltageOffsets)
                {
                    proposals.Add(new TuningProposal { Id = kvp.Key, Value = kvp.Value });
                }

                var request = new { array = proposals };
                var json = JsonConvert.SerializeObject(request);
                var response = await SendRpcAsync<string>(json, 270);

                var result = JsonConvert.DeserializeObject<TuningProposalResultJson>(response);
                var success = result?.Results?.TrueForAll(r => r.Result) == true;

                if (success)
                {
                    await ApplyChangesAsync(rebootRequired: false);
                    Logger.Info($"✅ 设置电压偏移成功");
                }

                return success;
            }
            catch (Exception ex)
            {
                Logger.Error($"设置电压偏移失败: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ApplyChangesAsync(bool rebootRequired = false)
        {
            try
            {
                var request = new { forceRestart = rebootRequired };
                var json = JsonConvert.SerializeObject(request);
                var result = await SendRpcAsync<bool>(json, 271);
                Logger.Info($"应用更改: {(result ? "成功" : "失败")}");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error($"应用更改失败: {ex.Message}");
                return false;
            }
        }

        private async Task<T> SendRpcAsync<T>(string message, uint commandCode)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("未连接到 XTU 服务");
            }

            try
            {
                var requestData = Encoding.UTF8.GetBytes(message);
                var lengthPrefix = BitConverter.GetBytes(requestData.Length);
                var packet = new byte[4 + requestData.Length];
                Buffer.BlockCopy(lengthPrefix, 0, packet, 0, 4);
                Buffer.BlockCopy(requestData, 0, packet, 4, requestData.Length);

                await _pipe.WriteAsync(packet, 0, packet.Length);
                await _pipe.FlushAsync();

                var buffer = new byte[4096];
                var bytesRead = await _pipe.ReadAsync(buffer, 0, buffer.Length);
                var responseJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                var response = JObject.Parse(responseJson);
                if (response["error"] != null)
                {
                    throw new Exception($"RPC 错误: {response["error"]}");
                }

                return response["result"].ToObject<T>();
            }
            catch (Exception ex)
            {
                Logger.Error($"RPC 通信失败 (命令码 {commandCode}): {ex.Message}");
                throw;
            }
        }

        private bool IsServiceRunningWmi()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT State FROM Win32_Service WHERE Name = '{ServiceName}'");
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    return obj["State"]?.ToString() == "Running";
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"WMI 查询服务状态失败: {ex.Message}");
            }
            return false;
        }

        private bool StartServiceWmi()
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT * FROM Win32_Service WHERE Name = '{ServiceName}'");
                foreach (System.Management.ManagementObject obj in searcher.Get())
                {
                    var result = obj.InvokeMethod("StartService", new object[0]);
                    var returnValue = Convert.ToInt32(result);
                    if (returnValue == 0)
                    {
                        Logger.Info("✅ XTU3SERVICE 启动成功");
                        return true;
                    }
                    else
                    {
                        Logger.Error($"启动服务失败,返回码: {returnValue}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"WMI 启动服务失败: {ex.Message}");
            }
            return false;
        }

        private string GetControlName(uint id)
        {
            return id switch
            {
                0x1F0 => "CPU 核心倍频",
                0x1F1 => "CPU 电压偏移",
                0x1F2 => "缓存/Ring 倍频",
                0x1F3 => "缓存电压偏移",
                0x1F4 => "GPU 频率",
                0x1F5 => "GPU 电压偏移",
                0x1F6 => "内存频率",
                0x1F7 => "内存电压",
                _ => $"未知控制项 (0x{id:X})"
            };
        }

        public void Dispose()
        {
            _pipe?.Dispose();
            _isConnected = false;
        }
    }

    public class OverclockingInfo
    {
        public bool IsOverclockSupported { get; set; }
        public bool IsSystemUnlocked { get; set; }
        public bool IsTurboBoostEnabled { get; set; }
        public bool IsCoreOcEnabled { get; set; }
        public bool IsClrOcEnabled { get; set; }
        public string ServiceVersion { get; set; }
        public uint PhysicalCoreCount { get; set; }
        public uint EfficientCoreCount { get; set; }
    }

    public class TuningControl
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public decimal DefaultValue { get; set; }
        public decimal ActiveValue { get; set; }
        public decimal MinValue { get; set; }
        public decimal MaxValue { get; set; }
        public bool Enabled { get; set; }
        public bool ReadOnly { get; set; }
        public bool RequiresReboot { get; set; }
        public List<decimal> SupportedValues { get; set; }
    }

    public class TuningProposal
    {
        public uint Id { get; set; }
        public decimal Value { get; set; }
    }

    public class TuningProposalResult
    {
        public uint Id { get; set; }
        public decimal Value { get; set; }
        public bool Enabled { get; set; }
        public bool RebootRequired { get; set; }
        public bool Result { get; set; }
    }

    public class TuningControlJson
    {
        [JsonProperty("array")]
        public List<TuningControlData> Controls { get; set; }

        [JsonProperty("array1")]
        public List<decimal> SupportedValues { get; set; }
    }

    public class TuningControlData
    {
        public uint Id { get; set; }
        public decimal DefaultValue { get; set; }
        public decimal ActiveValue { get; set; }
        public bool RequiresReboot { get; set; }
        public bool ReadOnly { get; set; }
        public bool Enabled { get; set; }
        public decimal Ratio { get; set; }
        public decimal MinPossibleValue { get; set; }
        public decimal MaxPossibleValue { get; set; }
    }

    public class TuningProposalResultJson
    {
        [JsonProperty("array")]
        public List<TuningProposalResult> Results { get; set; }
    }
}


