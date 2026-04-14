//#define ENABLE_LOG
/*
 * Copyright (c) 2014 - 2022 t_saki@serenegiant.com
 */

using AOT;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
#if UNITY_ANDROID && UNITY_2018_3_OR_NEWER
using UnityEngine.Android;
#endif

namespace Serenegiant.UVC
{
    [RequireComponent(typeof(AndroidUtils))]
    public class UVCManager : MonoBehaviour
    {
        private const string TAG = "UVCManager#";
        private const string FQCN_DETECTOR = "com.serenegiant.usb.DeviceDetector";

        //--------------------------------------------------------------------------------
        // Camera Terminal Descriptor's bmControls field bitmasks
        private const UInt64 CTRL_SCANNING		= 0x00000001;	// D0:  Scanning Mode
        private const UInt64 CTRL_AE		= 0x00000002;	// D1:  Auto-Exposure Mode
        private const UInt64 CTRL_AE_PRIORITY	= 0x00000004;	// D2:  Auto-Exposure Priority
        private const UInt64 CTRL_AE_ABS		= 0x00000008;	// D3:  Exposure Time (Absolute)
        private const UInt64 CTRL_FOCUS_ABS		= 0x00000020;	// D5:  Focus (Absolute)
        private const UInt64 CTRL_IRIS_ABS		= 0x00000080;	// D7:  Iris (Absolute)
        private const UInt64 CTRL_ZOOM_ABS		= 0x00000200;	// D9:  Zoom (Absolute)
        private const UInt64 CTRL_PANTILT_ABS	= 0x00000800;	// D11: PanTilt (Absolute)
        private const UInt64 CTRL_PAN_ABS		= 0x01000800;	// D11: PanTilt (Absolute)
        private const UInt64 CTRL_TILT_ABS		= 0x02000800;	// D11: PanTilt (Absolute)
        private const UInt64 CTRL_ROLL_ABS		= 0x00002000;	// D13: Roll (Absolute)
        private const UInt64 CTRL_D15		= 0x00008000;	// D15: Reserved
        private const UInt64 CTRL_D16		= 0x00010000;	// D16: Reserved
        private const UInt64 CTRL_FOCUS_AUTO	= 0x00020000;	// D17: Focus, Auto

        // Processing Unit Descriptor's bmControls field bitmasks
        private const UInt64 PU_BRIGHTNESS		= 0x00000001;	// D0: Brightness
        private const UInt64 PU_CONTRAST		= 0x00000002;	// D1: Contrast
        private const UInt64 PU_HUE			= 0x00000004;	// D2: Hue
        private const UInt64 PU_SATURATION		= 0x00000008;	// D3: Saturation
        private const UInt64 PU_SHARPNESS		= 0x00000010;	// D4: Sharpness
        private const UInt64 PU_GAMMA			= 0x00000020;	// D5: Gamma
        private const UInt64 PU_WB_TEMP		= 0x00000040;	// D6: White Balance Temperature
        private const UInt64 PU_WB_COMPO		= 0x00000080;	// D7: White Balance Component
        private const UInt64 PU_BACKLIGHT		= 0x00000100;	// D8: Backlight Compensation
        private const UInt64 PU_GAIN			= 0x00000200;	// D9: Gain
        private const UInt64 PU_POWER_LF		= 0x00000400;	// D10: Power Line Frequency
        private const UInt64 PU_HUE_AUTO		= 0x00000800;	// D11: Hue, Auto
        private const UInt64 PU_WB_TEMP_AUTO	= 0x00001000;	// D12: White Balance Temperature, Auto
        private const UInt64 PU_WB_COMPO_AUTO	= 0x00002000;	// D13: White Balance Component, Auto
        private const UInt64 PU_CONTRAST_AUTO	= 0x00040000;	// D18: Contrast, Auto

        // Set most significant bit to identify Processing Unit control type (プロセッシングユニットのコントロールタイプを識別するために最上位ビットを立てる)
        private const UInt64 PU_MASK = 0x80000000;

        //--------------------------------------------------------------------------------
        private static readonly UInt64[] SUPPORTED_CTRLS = {
            CTRL_SCANNING,
            CTRL_AE,
            CTRL_AE_PRIORITY,
            CTRL_AE_ABS,
            CTRL_FOCUS_ABS,
            CTRL_IRIS_ABS,
            CTRL_ZOOM_ABS,
            CTRL_PAN_ABS,
            CTRL_TILT_ABS,
            CTRL_ROLL_ABS,
            CTRL_FOCUS_AUTO,
        };
        private static readonly UInt64[] SUPPORTED_PROCS =
        {
            PU_BRIGHTNESS,
            PU_CONTRAST,
            PU_HUE,
            PU_SATURATION,
            PU_SHARPNESS,
            PU_GAMMA,
            PU_WB_TEMP,
            PU_WB_COMPO,
            PU_BACKLIGHT,
            PU_GAIN,
            PU_POWER_LF,
            PU_HUE_AUTO,
            PU_WB_TEMP_AUTO,
            PU_WB_COMPO_AUTO,
            PU_CONTRAST_AUTO,

        };

        //--------------------------------------------------------------------------------
        /**
			 * IUVCSelector is not set or IUVCSelector returns null during resolution selection,
			 * default resolution (width)
			 */
        public UInt32 DefaultWidth = 1280;
		/**
			 * IUVCSelector is not set or IUVCSelector returns null during resolution selection,
			 * default resolution (height)
			 */
		public UInt32 DefaultHeight = 720;
		/**
			 * Whether to negotiate H.264 during UVC device negotiation
			 * Only valid on Android devices
			 * true:	H.264 > MJPEG > YUV
			 * false:	MJPEG > H.264 > YUV
			 */
		public bool PreferH264 = false;
		/**
			 * Whether to get audio from UAC when possible
			 */
		public bool UACEnabled = false;
        /**
         * Whether to request UVC device image rendering to texture before scene rendering
         */
        public bool RenderBeforeSceneRendering = false;

		/**
			 * UVC-related event handlers
			 */
		[SerializeField, ComponentRestriction(typeof(IUVCDrawer))]
		public Component[] UVCDrawers;

		/**
			 * Holder class to hold camera information in use
			 */
		public class CameraInfo
		{
			internal readonly UVCDevice device;
			internal readonly UVCVideoSize[] SupportedSize;
			internal Texture previewTexture;
			internal volatile Int32 activeId;
			private bool isRenderBeforeSceneRendering;
            private bool isRendering;
			private Dictionary<UInt64, UVCCtrlInfo> ctrlInfos = new Dictionary<UInt64, UVCCtrlInfo>();
			public UVCVideoSize CurrentSize { get; private set; } = UVCVideoSize.INVALID;


			internal CameraInfo(UVCDevice device)
			{
				this.device = device;
				SupportedSize = UVCVideoSize.GetSupportedSize(device.id);
			}

			/**
				 * Get device id
				 */
			public Int32 Id{
					get { return device.id;  }
				}

			/**
				 * Get device name
				 */
			public string DeviceName
			{
				get { return device.name; }
			}

			/**
				 * Get vendor ID
				 */
			public int Vid
			{
				get { return device.vid; }
			}

			/**
				 * Get product ID
				 */
			public int Pid
			{
				get { return device.pid; }
			}

			/**
				 * Whether capturing video
				 */
			public bool IsPreviewing
			{
				get { return (activeId != 0) && (previewTexture != null); }
			}


			/**
				 * Change current resolution
				 * @param width
				 * @param height
				 */
			internal void SetSize(UVCVideoSize size)
			{
				CurrentSize = size;
			}

			/**
				 * Get resolution setting closest to specified conditions
				 * @param preferH264
				 * @param width
				 * @param height
				 */
			public UVCVideoSize FindNearest(bool preferH264, UInt32 width, UInt32 height)
			{
				if (SupportedSize.Length == 0)
				{	// Device is misbehaving (これにヒットするのは機器側がおかしい)
					throw new IndexOutOfRangeException();
				}

				UInt32[] frameTypes = {
					preferH264 ? UVCVideoSize.FRAME_TYPE_H264 : UVCVideoSize.FRAME_TYPE_MJPEG,
					preferH264 ? UVCVideoSize.FRAME_TYPE_H264_FRAME : UVCVideoSize.FRAME_TYPE_H264,
					preferH264 ? UVCVideoSize.FRAME_TYPE_MJPEG : UVCVideoSize.FRAME_TYPE_UNKNOWN,
					UVCVideoSize.FRAME_TYPE_UNKNOWN,	// ← This matches any frame type (これは任意のフレームタイプと一致)
				};

				var found = UVCVideoSize.INVALID;
				foreach (var frameType in frameTypes)
				{
					found = UVCVideoSize.FindNearest(SupportedSize, frameType, width, height);
					if (found.IsValid || (frameType == UVCVideoSize.FRAME_TYPE_UNKNOWN))
					{	// Found resolution setting or at last of frame type array (解像度設定が見つかるかフレームタイプ配列の最後なら break)
						break;
					}
				}
				if (!found.IsValid)
				{	// Select first (default resolution setting) if not found (見つからなければ先頭 (= デフォルトの解像度設定のはず) を選択)
					found = SupportedSize[0];
				}

				return found;
			}

			/**
				 * Update information of supported UVC controls/processing functions
				 */
			public void UpdateCtrls()
			{
				ctrlInfos.Clear();
				var ctrls = GetCtrlSupports(Id);
				foreach (UInt64 ctrl in SUPPORTED_CTRLS)
				{
					if ((ctrls & ctrl) == ctrl)
					{
						UVCCtrlInfo info = new UVCCtrlInfo();
						info.type = ctrl;
						if (GetCtrlInfo(Id, ref info) == 0)
						{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
							Console.WriteLine($"{TAG}ctrl({ctrl:X}):={info}");
#endif
							ctrlInfos.Add(info.type, info);
						}
					}
				}
				ctrls = GetProcSupports(Id);
				foreach (UInt64 ctrl in SUPPORTED_PROCS)
				{
					if ((ctrls & ctrl) == ctrl)
					{
						UVCCtrlInfo info = new UVCCtrlInfo();
						info.type = ctrl | PU_MASK;
						if (GetCtrlInfo(Id, ref info) == 0)
						{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
							Console.WriteLine($"{TAG}proc({ctrl:X}):={info}");
#endif
							ctrlInfos.Add(info.type, info);
						}
					}
				}
			}

			/**
				 * Get list of supported UVC control/processing function types
				 */
			public List<UInt64> GetCtrls()
			{
				return new List<UInt64>(ctrlInfos.Keys);
			}

			/**
				 * Get information of specified UVC control/processing function
				 * @param type
				 * @return UVCCtrlInfo
				 * @throws ArgumentOutOfRangeException
				 */
			public UVCCtrlInfo GetInfo(UInt64 type)
			{
				if (ctrlInfos.ContainsKey(type))
				{
					return ctrlInfos.GetValueOrDefault(type, new UVCCtrlInfo());
				} else
				{
					throw new ArgumentOutOfRangeException($"Not supported control type{type:X}");
				}
			}

			/**
				 * Get setting value of UVC control/processing function
				 * @param type
				 * @return Changed value
				 * @throws ArgumentOutOfRangeException
				 * @throws Exception
				 */
			public Int32 GetValue(UInt64 type)
			{
				if (ctrlInfos.ContainsKey(type))
				{
					Int32 value = 0;
					var r = GetCtrlValue(Id, type, ref value);
					if (r == 0)
					{
						return value;
					} else
					{
						throw new Exception($"Failed to get control value,type={type},err={r}");
					}
				} else
				{
					throw new ArgumentOutOfRangeException($"Not supported control type{type:X}");
				}
			}

			/**
				 * Change setting of UVC control/processing function
				 * @param type
				 * @param value
				 * @return Changed value
				 * @throws ArgumentOutOfRangeException
				 * @throws Exception
				 */
			public Int32 SetValue(UInt64 type, Int32 value)
			{
				if (ctrlInfos.ContainsKey(type))
				{
					var r = SetCtrlValue(Id, type, value);
					if (r == 0)
					{
						r = GetCtrlValue(Id, type, ref value);
						if (r == 0)
						{
							var info = ctrlInfos.GetValueOrDefault(type, new UVCCtrlInfo());
							info.current = value;
							ctrlInfos[type] = info;
							return value;
						}
						else
						{
							throw new Exception($"Failed to get control value,type={type},err={r}");
						}
					}
					else
					{
						throw new Exception($"Failed to set control value,type={type},err={r}");
					}
				}
				else
				{
					throw new ArgumentOutOfRangeException($"Not supported control type{type:X}");
				}
			}

			public override string ToString()
			{
				return $"{base.ToString()}(id={Id},activeId={activeId},IsPreviewing={IsPreviewing},sz={CurrentSize})";
			}

            /**
             * Start rendering video from UVC device
             * @param manager
             */
            internal Coroutine StartRender(UVCManager manager, bool renderBeforeSceneRendering)
            {
                StopRender(manager);
                isRenderBeforeSceneRendering = renderBeforeSceneRendering;
                isRendering = true;
                if (renderBeforeSceneRendering)
                {
                    return manager.StartCoroutine(OnRenderBeforeSceneRendering());
                } else
                {
                    return manager.StartCoroutine(OnRender());
                }
            }

            /**
             * Stop rendering video from UVC device
             * @param manager
             */
            internal void StopRender(UVCManager manager)
            {
                if (isRendering)
                {
                    isRendering = false;
                    if (isRenderBeforeSceneRendering)
                    {
                        manager.StopCoroutine(OnRenderBeforeSceneRendering());
                    }
                    else
                    {
                        manager.StopCoroutine(OnRender());
                    }
                }
            }

            /**
				 * For render event processing
				 * Executed as coroutine
				 * Request rendering of video from UVC device to texture before scene rendering
				 */
            private IEnumerator OnRenderBeforeSceneRendering()
				{
					var renderEventFunc = GetRenderEventFunc();
					for (; activeId != 0;)
					{
						yield return null;
						GL.IssuePluginEvent(renderEventFunc, activeId);
					}
					yield break;
				}

            /**
             * For render event processing
             * Executed as coroutine
             * Request rendering of video from UVC device to texture after rendering
             */
            private IEnumerator OnRender()
            {
                var renderEventFunc = GetRenderEventFunc();
                for (; activeId != 0;)
                {
                    yield return new WaitForEndOfFrame();
                    GL.IssuePluginEvent(renderEventFunc, activeId);
                }
                yield break;
            }

		} // CameraInfo

		/**
			 * Holder class to hold objects related to audio capture from UAC device
			 */
		public class AudioInfo
		{
			internal readonly UVCDevice device;
			private UACInfo info = new UACInfo();
			private int samplesPerFrame = 0;
			private Int16[] buffer;	// Currently only PCM16 is supported. Memory blocks may become discontinuous or move if kept here (今のところ PCM16 にしか対応しない，OnPCM16Read ないではなくここで保持するともしかするとメモリーブロックが不連続になったり移動したりするかもしれないけど)
			private volatile AudioClip audioClip;
			private volatile Int32 activeId;

			internal AudioInfo(UVCDevice device)
			{
				this.device = device;
			}


			/**
				 * Whether capturing audio
				 */
			public bool IsStreaming
			{
				get { return (activeId != 0) && (audioClip != null); }
			}

			/**
				 * Start audio capture
				 * Do nothing if already capturing audio
				 * @param manager
				 */
			internal AudioClip Start(UVCManager manager)
			{
				var result = StartUAC(device.id);
				if (result == 0)
				{
					if (GetUACInfo(device.id, ref info) == 0)
					{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
						Console.WriteLine($"{TAG}Start:info={info}");
#endif
						try
						{
							// PCM16 (PCM16 のはず)
							activeId = device.id;
							samplesPerFrame = info.packetBytes / (info.resolution / 8);
							buffer = new Int16[samplesPerFrame];
							audioClip = AudioClip.Create(device.name, Int32.MaxValue, info.channels, info.samplingFreq, true, OnPCM16Read);
							return audioClip;
						}
						catch (Exception e)
						{
							activeId = 0;
#if (!NDEBUG && DEBUG && ENABLE_LOG)
							Console.WriteLine($"Failed to create audio clip,err={e}");
#endif
							throw e;
						}
					}
					else
					{
						manager.StopAudio(device);
						throw new Exception($"Failed to get streaming info,err={result}");
					}
				}
				else
				{
					throw new Exception($"Failed to start uac streaming,err={result}");
				}
			}

			/**
				 * Stop audio capture
				 * @param manager
				 */
			internal void Stop(UVCManager manager)
			{
				activeId = 0;
				audioClip = null;
				manager.StopAudio(device);
			}

#if (!NDEBUG && DEBUG && ENABLE_LOG)
			private int readCnt = 0;
#endif

			/**
				 * Callback from AudioClip
				 * @param data
				 */
			private void OnPCM16Read(float[] data)
			{
				if (!IsStreaming) return;

				var numSamples = data.Length;					// Maximum number of samples to read this time (今回読み取る最大サンプル数)
				var maxReadCnt = numSamples / samplesPerFrame;	// Maximum number of read operations (最大読み込み回数)
				if (maxReadCnt == 0)
				{
					maxReadCnt = 1;
				}
				var result = -1;
				Int64 ptsUs = 0;
				//var buffer = new Int16[samplesPerFrame];	// Changed to allocate at Start for performance (高速化のために Start でアロケーションするように変更した)
				Int32 dataBytes = 0;
				Int32 totalSamples = 0;
				for (int i = 0; (i < maxReadCnt) && (totalSamples < numSamples); i++)
				{
					result = GetUACFrame(device.id, buffer, ref dataBytes, ref ptsUs);
					if ((result == 0) && (dataBytes >= 2))
					{
						var samples = Math.Min(dataBytes / 2, samplesPerFrame);
						for (int j = 0; j < samples; j++)
						{
							data[j + totalSamples] = buffer[j] / (float)short.MaxValue;
						}
						totalSamples += samples;
					}
					else
					{
						break;
					}
				}
				if (totalSamples < numSamples)
				{
					Array.Resize<float>(ref data, totalSamples);
				}
#if (!NDEBUG && DEBUG && ENABLE_LOG)
				if ((readCnt++ % 100) == 0)
				{
					Console.WriteLine($"{TAG}OnPCM16Read:len={numSamples},total={totalSamples},r={result},bytes={dataBytes},pts={ptsUs}");
				}
#endif
			}

		} // AudioInfo

		/**
			 * SynchronizationContext instance for execution on main thread
			 */
		private SynchronizationContext mainContext;
		/**
			 * Delegate to receive event callbacks when UVC device connection state changes on device
			 */
		private PluginCallbackManager.OnDeviceChangedFunc callback;
		/**
			 * List of UVC devices connected to device
			 */
		private List<UVCDevice> attachedDevices = new List<UVCDevice>();
		/**
			 * Map of UVC devices currently capturing video
			 * Holds id - CameraInfo pair for device identification
			 */
		private Dictionary<Int32, CameraInfo> cameraInfos = new Dictionary<int, CameraInfo>();

		/**
			 * Map of UVC devices currently capturing audio
			 * Holds id - AudioInfo pair for device identification
			 */
		private Dictionary<Int32, AudioInfo> audioInFos = new Dictionary<int, AudioInfo>();

		//--------------------------------------------------------------------------------
		// Called from UnityEngine
		//--------------------------------------------------------------------------------
		// Start is called before the first frame update
		IEnumerator Start()
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}Start:");
#endif
			mainContext = SynchronizationContext.Current;
            callback = PluginCallbackManager.Add(this);

			yield return Initialize();
		}

#if (!NDEBUG && DEBUG && ENABLE_LOG)
		void OnApplicationFocus()
		{
			Console.WriteLine($"{TAG}OnApplicationFocus:");
		}
#endif

#if (!NDEBUG && DEBUG && ENABLE_LOG)
		void OnApplicationPause(bool pauseStatus)
		{
			Console.WriteLine($"{TAG}OnApplicationPause:{pauseStatus}");
		}
#endif

#if (!NDEBUG && DEBUG && ENABLE_LOG)
		void OnApplicationQuits()
		{
			Console.WriteLine($"{TAG}OnApplicationQuits:");
		}
#endif

		void OnDestroy()
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}OnDestroy:");
#endif
			StopAll();
            PluginCallbackManager.Remove(this);
		}

		//--------------------------------------------------------------------------------
		// Plugin callback function for UVC device connection state changes
		//--------------------------------------------------------------------------------
        public void OnDeviceChanged(Int32 deviceId, bool attached)
        {
            var id = deviceId;
#if (!NDEBUG && DEBUG && ENABLE_LOG)
            Console.WriteLine($"{TAG}OnDeviceChangedInternal:id={id},attached={attached}");
#endif
            if (attached)
            {
                UVCDevice device = new UVCDevice(deviceId);
#if (!NDEBUG && DEBUG && ENABLE_LOG)
                Console.WriteLine($"{TAG}OnDeviceChangedInternal:device={device.ToString()}");
#endif
                if (HandleOnAttachEvent(device))
                {
                    attachedDevices.Add(device);
                    StartPreview(device, UVCVideoSize.INVALID);
					if (UACEnabled)
					{	// If UVCManager's UAC function is enabled
						StartAudio(device);
					}
				}
            }
            else
            {
                var found = attachedDevices.Find(item =>
                {
                    return item != null && item.id == id;
                });
                if (found != null)
                {
                    HandleOnDetachEvent(found);
                    StopPreview(found);
					StopAudio(found);
					RemoveCamera(found);
					RemoveAudio(found);
                    attachedDevices.Remove(found);
                }
            }
        }

		//================================================================================
		/**
			 * Get list of connected UVC devices
			 * @return List of connected UVC devices
			 */
		public List<CameraInfo> GetAttachedDevices()
		{
			return new List<CameraInfo>(cameraInfos.Values);
		}


		/**
			 * Change resolution
			 * @param Specify UVC device to change resolution
			 * @param Specify resolution to change to
			 * @param Whether resolution was changed
			 */
		public bool SetVideoSize(UVCDevice device, UVCVideoSize size)
		{
			var info = GetCamera(device);
			if (info != null)
			{
				if (size.IsValid && info.IsPreviewing && !size.IsSameValue(info.CurrentSize))
				{	// When resolution is changed
					StopPreview(device);
					StartPreview(device, size);
					return true;
				}
				info.SetSize(size);
			}
			return false;
		}

		/**
			 * Start video capture from UVC device
			 * @param device
			 * @param size
			 */
		private void StartPreview(UVCDevice device, UVCVideoSize size)
		{
			var info = CreateCameraIfNotExist(device);
			if ((info != null) && !info.IsPreviewing) {
				if (!size.IsValid)
				{	// When resolution setting is invalid, try to get from CameraInfo
					size = info.CurrentSize;
				}
				if (!size.IsValid)
				{	// If resolution setting from CameraInfo is invalid, search from supported resolutions
					size = info.FindNearest(PreferH264, DefaultWidth, DefaultHeight);
				}
				if (!size.IsValid)
				{	// Check just in case (should not reach here)
					throw new ArgumentException("Video size not found");
				}
#if (!NDEBUG && DEBUG && ENABLE_LOG)
				Console.WriteLine($"{TAG}StartPreview:id={device.id},sz={size}");
#endif
				if (Resize(device.id, size.FrameType, size.Width, size.Height) == 0)
                {
					info.SetSize(size);
					info.activeId = device.id;
					info.UpdateCtrls();
					mainContext.Post(__ =>
					{		// Texture generation must be done on main thread
#if (!NDEBUG && DEBUG && ENABLE_LOG)
						Console.WriteLine($"{TAG}Video capture texture generation:({size.Width}x{size.Height})");
#endif
						var tex = new Texture2D(
							Convert.ToInt32(size.Width), Convert.ToInt32(size.Height),
							TextureFormat.ARGB32,
							false, /* mipmap */
							true /* linear */);
						tex.filterMode = FilterMode.Point;
						tex.Apply();
						info.previewTexture = tex;
						var nativeTexPtr = info.previewTexture.GetNativeTexturePtr();
						Start(device.id, nativeTexPtr, tex.width, tex.height);
						HandleOnStartPreviewEvent(info);
						info.StartRender(this, RenderBeforeSceneRendering);
					}, null);
				}
				else
				{	// Check just in case (should not reach here)
					throw new ArgumentException("Wrong video size");
				}
			}
		}

		/**
			 * Stop video capture from UVC device
			 */
		private void StopPreview(UVCDevice device) {
			var info = GetCamera(device);
			if ((info != null) && info.IsPreviewing)
			{
				mainContext.Post(__ =>
				{
					HandleOnStopPreviewEvent(info);
					Stop(device.id);
					info.StopRender(this);
					info.SetSize(UVCVideoSize.INVALID);
					info.activeId = 0;
				}, null);
			}
		}


		/**
			 * Start audio capture from UAC device
			 * @param device
			 */
		private void StartAudio(UVCDevice device)
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}StartAudio:");
#endif
			if (device.isUAC)
			{
				var audio = CreateAudioIfNotExist(device);
				if ((audio != null) && !audio.IsStreaming)
				{
					mainContext.Post(__ =>
					{
						var audioClip = audio.Start(this);
						HandleOnStartAudioEvent(audio, audioClip);
					}, null);

				}
			}
			else
			{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
				Console.WriteLine($"{TAG}StartAudio:Not a UAC device");
#endif
			}
		}

		/**
			 * Stop audio capture from UAC device
			 * @param device
			 */
		private void StopAudio(UVCDevice device)
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}StopAudio:");
#endif
			var audio = GetAudio(device);
			if (audio != null && audio.IsStreaming)
			{
				mainContext.Post(__ =>
				{
					HandleOnStopAudioEvent(audio);
					audio.Stop(this);
				}, null);
			}
		}

		/**
			 * Stop video and audio capture from all connected UVC/UAC devices
			 */
		private void StopAll() {
			List<CameraInfo> values = new List<CameraInfo>(cameraInfos.Values);
			foreach (var info in values)
			{
				StopAudio(info.device);
				StopPreview(info.device);
			}
		}

		//--------------------------------------------------------------------------------
		/**
			 * Handler for when UVC device is connected
			 * @param info
			 * @return true: Use connected UVC device, false: Do not use connected UVC device
			 */
		private bool HandleOnAttachEvent(UVCDevice device/*NonNull*/)
		{
			if ((UVCDrawers == null) || (UVCDrawers.Length == 0))
			{		// Return true (use connected UVC device) if IUVCDrawer is not assigned
				return true;
			}
			else
			{
				bool hasDrawer = false;
				foreach (var drawer in UVCDrawers)
				{
					if (drawer is IUVCDrawer)
					{
						hasDrawer = true;
						if ((drawer as IUVCDrawer).OnUVCAttachEvent(this, device))
						{		// Return true (use connected UVC device) if any IUVCDrawer returns true
							return true;
						}
					}
				}
				// Return true (use connected UVC device) if IUVCDrawer is not assigned
				return !hasDrawer;
			}
		}

		/**
			 * Handler for when UVC device is detached
			 * @param info
			 */
		private void HandleOnDetachEvent(UVCDevice device/*NonNull*/)
		{
			if ((UVCDrawers != null) && (UVCDrawers.Length > 0))
			{
				foreach (var drawer in UVCDrawers)
				{
					if (drawer is IUVCDrawer)
					{
						(drawer as IUVCDrawer).OnUVCDetachEvent(this, device);
					}
				}
			}
		}

		/**
			 * Video capture from UVC device has started
			 * @param args UVC device identifier string
			 */
		void HandleOnStartPreviewEvent(CameraInfo camera)
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}HandleOnStartPreviewEvent:({camera})");
#endif
			if ((camera != null) && camera.IsPreviewing && (UVCDrawers != null))
			{
				foreach (var drawer in UVCDrawers)
				{
					if ((drawer is IUVCDrawer) && (drawer as IUVCDrawer).IsUVCEnabled(this, camera.device))
					{
						(drawer as IUVCDrawer).OnUVCStartEvent(this, camera.device, camera.previewTexture);
					}
				}
			} else {
#if (!NDEBUG && DEBUG && ENABLE_LOG)
				Console.WriteLine($"{TAG}HandleOnStartPreviewEvent:No UVCDrawers");
#endif
			}
		}

		/**
			 * Video capture from UVC device has ended
			 * @param args UVC device identifier string
			 */
		void HandleOnStopPreviewEvent(CameraInfo camera)
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}HandleOnStopPreviewEvent:({camera})");
#endif
			if (UVCDrawers != null)
			{
				foreach (var drawer in UVCDrawers)
				{
					if ((drawer is IUVCDrawer) && (drawer as IUVCDrawer).IsUVCEnabled(this, camera.device))
					{
						(drawer as IUVCDrawer).OnUVCStopEvent(this, camera.device);
					}
				}
			}
		}

		void HandleOnStartAudioEvent(AudioInfo audio, AudioClip audioClip)
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}HandleOnStartAudioEvent:({audio})");
#endif
			if ((audio != null) && audio.IsStreaming && (UVCDrawers != null))
			{
				foreach (var drawer in UVCDrawers)
				{
					if ((drawer is IUVCDrawer) && (drawer as IUVCDrawer).IsUACEnabled(this, audio.device))
					{		// Only IUVCDrawer that returns true for IsUACEnabled calls OnUACStartEvent
						(drawer as IUVCDrawer).OnUACStartEvent(this, audio.device, audioClip);
					}
				}
			}
		}

		void HandleOnStopAudioEvent(AudioInfo audio)
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}HandleOnStopAudioEvent:({audio})");
#endif
			if (UVCDrawers != null)
			{
				foreach (var drawer in UVCDrawers)
				{
					if ((drawer is IUVCDrawer) && (drawer as IUVCDrawer).IsUACEnabled(this, audio.device))
					{		// Only IUVCDrawer that returns true for IsUACEnabled calls OnUACStopEvent
						(drawer as IUVCDrawer).OnUACStopEvent(this, audio.device);
					}
				}
			}
		}

		//--------------------------------------------------------------------------------
		/**
			 * Get CameraInfo for specified UVC identifier string
			 * Create new if not registered yet
			 * @param deviceName UVC device identifier string
			 * @param CameraInfo to return
			 */
		/*NonNull*/
		private CameraInfo CreateCameraIfNotExist(UVCDevice device)
		{
			if (!cameraInfos.ContainsKey(device.id))
			{
				cameraInfos[device.id] = new CameraInfo(device);
			}
			return cameraInfos[device.id];
		}

		/**
			 * Get CameraInfo for specified UVC identifier string
			 * @param device
			 * @param Return CameraInfo if registered, null if not registered
			 */
		/*Nullable*/
		private CameraInfo GetCamera(UVCDevice device)
		{
			return cameraInfos.ContainsKey(device.id) ? cameraInfos[device.id] : null;
		}

		/**
			 * Remove CameraInfo for specified UVCDevice
			 * @param device
			 * @param Corresponding AudioInfo or null
			 */
		private CameraInfo RemoveCamera(UVCDevice device)
		{
			var result = GetCamera(device);
			cameraInfos.Remove(device.id);

			return result;
		}

		/**
			 * Get CameraInfo for specified UVC identifier string
			 * Create new if not registered yet
			 * @param deviceName UVC device identifier string
			 * @param CameraInfo to return
			 */
		/*NonNull*/
		private AudioInfo CreateAudioIfNotExist(UVCDevice device)
		{
			if (!audioInFos.ContainsKey(device.id))
			{
				audioInFos[device.id] = new AudioInfo(device);
			}
			return audioInFos[device.id];
		}

		/**
			 * Get AudioInfo for specified UVCDevice
			 * @param device
			 * @param Return AudioInfo if registered, null if not registered
			 */
		/*Nullable*/
		private AudioInfo GetAudio(UVCDevice device)
		{
			return audioInFos.ContainsKey(device.id) ? audioInFos[device.id] : null;
		}

		/**
			 * Remove AudioInfo for specified UVCDevice
			 * @param device
			 * @param Corresponding AudioInfo or null
			 */
		private AudioInfo RemoveAudio(UVCDevice device)
		{
			var result = GetAudio(device);
			audioInFos.Remove(device.id);

			return result;
		}

		//--------------------------------------------------------------------------------
		/**
			 * Initialize plugin
			 * Check permission and call actual plugin initialization #InitPlugin if permission is granted
			 */
		private IEnumerator Initialize()
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}Initialize:");
#endif
			if (AndroidUtils.CheckAndroidVersion(28))
			{
				yield return AndroidUtils.GrantCameraPermission((string permission, AndroidUtils.PermissionGrantResult result) =>
				{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
					Console.WriteLine($"{TAG}OnPermission:{permission}={result}");
#endif
					switch (result)
					{
						case AndroidUtils.PermissionGrantResult.PERMISSION_GRANT:
							InitPlugin();
							break;
						case AndroidUtils.PermissionGrantResult.PERMISSION_DENY:
							if (AndroidUtils.ShouldShowRequestPermissionRationale(AndroidUtils.PERMISSION_CAMERA))
							{
								// Permission was not granted
								// FIXME Need to show explanation dialog
							}
							break;
						case AndroidUtils.PermissionGrantResult.PERMISSION_DENY_AND_NEVER_ASK_AGAIN:
							break;
					}
				});
			}
			else
			{
				InitPlugin();
			}

			yield break;
		}

		/**
			 * Initialize plugin
			 * Request processing to uvc-plugin-unity
			 */
		private void InitPlugin()
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}InitPlugin:");
#endif
			// Check if IUVCDrawers is assigned
			var hasDrawer = false;
			if ((UVCDrawers != null) && (UVCDrawers.Length > 0))
			{
				foreach (var drawer in UVCDrawers)
				{
					if (drawer is IUVCDrawer)
					{
						hasDrawer = true;
						break;
					}
				}
			}
			if (!hasDrawer)
			{		// If IUVCDrawer is not set in inspector
				// Try to get from gameObject this script is added to
#if (!NDEBUG && DEBUG && ENABLE_LOG)
				Console.WriteLine($"{TAG}InitPlugin:has no IUVCDrawer, try to get from gameObject");
#endif
				var drawers = GetComponents(typeof(IUVCDrawer));
				if ((drawers != null) && (drawers.Length > 0))
				{
					UVCDrawers = new Component[drawers.Length];
					int i = 0;
					foreach (var drawer in drawers)
					{
						UVCDrawers[i++] = drawer;
					}
				}
			}
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}InitPlugin:num drawers={UVCDrawers.Length}");
#endif
			// Request to load DeviceDetector from aandusb
			using (AndroidJavaClass clazz = new AndroidJavaClass(FQCN_DETECTOR))
			{
				clazz.CallStatic("initUVCDeviceDetector",
					AndroidUtils.GetCurrentActivity());
			}
		}

        //--------------------------------------------------------------------------------
        // Definitions and declarations for native plugin
        //--------------------------------------------------------------------------------
		/**
			 * Native (c/c++) function for render event acquisition in plugin
			 */
		[DllImport("unityuvcplugin")]
		private static extern IntPtr GetRenderEventFunc();
        /**
			 * Initial configuration
			 */
        [DllImport("unityuvcplugin", EntryPoint = "Config")]
        private static extern Int32 Config(Int32 deviceId, Int32 enabled, Int32 useFirstConfig);
        /**
			 * Start video capture
			 */
        [DllImport("unityuvcplugin", EntryPoint ="Start")]
		private static extern Int32 Start(Int32 deviceId, IntPtr tex, Int32 width, Int32 height);
		/**
			 * Stop video capture
			 */
		[DllImport("unityuvcplugin", EntryPoint ="Stop")]
		private static extern Int32 Stop(Int32 deviceId);
		/**
			 * Set video size
			 */
		[DllImport("unityuvcplugin")]
		private static extern Int32 Resize(Int32 deviceId, UInt32 frameType, UInt32 width, UInt32 height);
        /**
			 * Get supported UVC control function mask
			 */
        [DllImport("unityuvcplugin")]
        private static extern UInt64 GetCtrlSupports(Int32 deviceId);
        /**
			 * Get supported UVC processing function mask
			 */
        [DllImport("unityuvcplugin")]
        private static extern UInt64 GetProcSupports(Int32 deviceId);
        /**
			 * Get information of supported UVC control/processing function
			 */
        [DllImport("unityuvcplugin", CallingConvention=CallingConvention.StdCall)]
        private static extern Int32 GetCtrlInfo(Int32 deviceId, ref UVCCtrlInfo info);
        /**
			 * Get setting value of supported UVC control/processing function
			 */
        [DllImport("unityuvcplugin")]
        private static extern Int32 GetCtrlValue(Int32 deviceId, UInt64 ctrl, ref Int32 value);
        /**
			 * Set setting value of supported UVC control/processing function
			 */
        [DllImport("unityuvcplugin")]
        private static extern Int32 SetCtrlValue(Int32 deviceId, UInt64 ctrl, Int32 value);
        /**
			 * Start audio capture from UAC
			 */
        [DllImport("unityuvcplugin")]
        private static extern Int32 StartUAC(Int32 deviceId);
        /**
			 * Stop audio capture from UAC
			 */
        [DllImport("unityuvcplugin")]
        private static extern Int32 StopUAC(Int32 deviceId);
        /**
			 * Get information of supported UVC control/processing function
			 */
        [DllImport("unityuvcplugin", CallingConvention = CallingConvention.StdCall)]
        private static extern Int32 GetUACInfo(Int32 deviceId, ref UACInfo info);
		/**
			 * Get audio data from UAC (blocks calling thread for up to 500ms)
			 */
		[DllImport("unityuvcplugin", CallingConvention = CallingConvention.StdCall)]
		private static extern Int32 GetUACFrame(Int32 deviceId, short[] data, ref Int32 dataLen, ref Int64 ptsUs);

	}   // UVCManager

	/**
     * IL2Cpp cannot marshal delegate used for callback from c/c++,
     * so we must not use static class/function for processing.
     * But if we do, we cannot call function of calling object's, so create manager class
     * For now, only accepts UVCManager so not using interface
     */
	public static class PluginCallbackManager
    {
        // Declare type of callback function
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void OnDeviceChangedFunc(Int32 id, Int32 deviceId, bool attached);

        /**
			 * Plugin's native registration function
			 */
        [DllImport("unityuvcplugin")]
        private static extern IntPtr Register(Int32 id, OnDeviceChangedFunc deviceChanged);
        /**
			 * Plugin's native unregistration function
			 */
        [DllImport("unityuvcplugin")]
        private static extern IntPtr Unregister(Int32 id);

        private static Dictionary<Int32, UVCManager> sManagers = new Dictionary<Int32, UVCManager>();

        /**
         * Add specified UVCManager to connection device change callback
         */
        public static OnDeviceChangedFunc Add(UVCManager manager)
        {
            Int32 id = manager.GetHashCode();
			OnDeviceChangedFunc onDeviceChanged = new OnDeviceChangedFunc(OnDeviceChanged);
            sManagers.Add(id, manager);
            Register(id, onDeviceChanged);
            return onDeviceChanged;
        }

        /**
         * Remove specified UVCManager from connection device change callback
         */
        public static void Remove(UVCManager manager)
        {
            Int32 id = manager.GetHashCode();
            Unregister(id);
            sManagers.Remove(id);
        }

        [MonoPInvokeCallback(typeof(OnDeviceChangedFunc))]
        public static void OnDeviceChanged(Int32 id, Int32 deviceId, bool attached)
        {
            var manager = sManagers.ContainsKey(id) ? sManagers[id] : null;
            if (manager != null)
            {
                manager.OnDeviceChanged(deviceId, attached);
            }
        }

    } // PluginCallbackManager


}   // namespace Serenegiant.UVC
