//#define ENABLE_LOG
/*
 * Copyright (c) 2014 - 2022 t_saki@serenegiant.com
 */

using System;
using System.Runtime.InteropServices;

/*
 * THETA S  vid:1482, pid:1001
 * THETA V  vid:1482, pid:1002
 * THETA Z1 vid:1482, pid:1005
 */

namespace Serenegiant.UVC
{

	[Serializable]
	public class UVCDevice
	{
		public readonly Int32 id;
		public readonly int vid;
		public readonly int pid;
		public readonly int deviceClass;
		public readonly int deviceSubClass;
		public readonly int deviceProtocol;

		public readonly string name;

		public UVCDevice(Int32 deviceId) {
			id = deviceId;
			vid = GetVendorId(deviceId);
			pid = GetProductId(deviceId);
			name = GetName(deviceId);
			deviceClass = GetDeviceClass(deviceId);
			deviceSubClass = GetDeviceSubClass(deviceId);
			deviceProtocol = GetDeviceProtocol(deviceId);
		}

		public override string ToString()
		{
			return $"{base.ToString()}(id={id},vid={vid},pid={pid},name={name},deviceClass={deviceClass},deviceSubClass={deviceSubClass},deviceProtocol={deviceProtocol})";
		}


		/**
			 * Ricoh の製品かどうか (Whether it's a Ricoh product)
			 * @param info
			 */
		public bool IsRicoh
		{
			get { return (vid == 1482); }
		}

        /**
			 * THETA S/V/Z1 のいずれかかどうか (Whether it's THETA S/V/Z1)
			 */
        public bool IsTHETA
        {
            get { return IsTHETA_S || IsTHETA_V || IsTHETA_Z1; }
        }

		/**
			 * THETA S かどうか (Whether it's THETA S)
			 */
		public bool IsTHETA_S
		{
			get { return (vid == 1482) && (pid == 10001); }
		}

		/**
			 * THETA V かどうか (Whether it's THETA V)
			 */
		public bool IsTHETA_V
		{
			// THETA V からの pid=872 は UVC でなくて動かないので注意 (THETA 側は静止画/動画モード) (Note: pid=872 from THETA V is not UVC and won't work (THETA side is still image/video mode))
			get { return (vid == 1482) && (pid == 10002); }
		}

        /**
			 * THETA Z1 かどうか (Whether it's THETA Z1)
			 * @param info
			 */
        public bool IsTHETA_Z1
        {
            // THETA Z1 からの pid=877 は UVC ではなくて動かないので注意 (THETA 側は静止画/動画モード) (Note: pid=877 from THETA Z1 is not UVC and won't work (THETA side is still image/video mode))
            get { return (vid == 1482) && (pid == 10005); }
        }

		/**
			 * UAC に対応しているかどうか (Whether UAC is supported)
			 * XXX UAC に対応していると応答する UVC 機器でも実際には UAC に未対応なバギーな機器も存在するので注意！ (Note: some buggy UVC devices claim UAC support but don't actually support it!)
			 */
		public bool isUAC
		{
			get { return Match(1, 1, 0xff) && Match(1, 2, 0xff); }
		}

		/**
			 * デバイスまたはインターフェースが指定した条件に一致するかどうかを確認 (Check if device or interface matches specified conditions)
			 * @param bClass 0xff なら常にマッチする (ワイルドカード) (0xff always matches (wildcard))
			 * @param bSubClass 0xff なら常にマッチする (ワイルドカード) (0xff always matches (wildcard))
			 * @param bProtocol 0xff なら常にマッチする (ワイルドカード) (0xff always matches (wildcard))
			 * @returtn 1: 一致した，0: 一致しなかった (1: matched, 0: not matched)
			 */
		public bool Match(byte bClass, byte bSubClass, byte bProtocol)
		{
			var result = InternalMatch(id, bClass, bSubClass, bProtocol);
			return result != 0;
		}

		//--------------------------------------------------------------------------------
		// プラグインのインターフェース関数 (Plugin interface functions)
		//--------------------------------------------------------------------------------
		/**
			 * 機器 id を取得 (これだけは public にする) (Get device id (only this is public))
			 */
		[DllImport("unityuvcplugin", EntryPoint = "DeviceInfo_get_id"])
		public static extern Int32 GetId(Int32 deviceId);

		/**
				 * デバイスクラスを取得 (Get device class)
				 */
		[DllImport("unityuvcplugin", EntryPoint = "DeviceInfo_get_device_class"])
		private static extern Byte GetDeviceClass(Int32 deviceId);

		/**
				 * デバイスサブクラスを取得 (Get device subclass)
				 */
		[DllImport("unityuvcplugin", EntryPoint = "DeviceInfo_get_device_sub_class"])
		private static extern Byte GetDeviceSubClass(Int32 deviceId);

		/**
				 * デバイスプロトコルを取得 (Get device protocol)
				 */
		[DllImport("unityuvcplugin", EntryPoint = "DeviceInfo_get_device_protocol"])
		private static extern Byte GetDeviceProtocol(Int32 deviceId);

		/**
				 * ベンダー ID を取得 (Get vendor ID)
				 */
		[DllImport("unityuvcplugin", EntryPoint = "DeviceInfo_get_vendor_id"])
		private static extern UInt16 GetVendorId(Int32 deviceId);

		/**
				 * プロダクト ID を取得 (Get product ID)
				 */
		[DllImport("unityuvcplugin", EntryPoint = "DeviceInfo_get_product_id"])
		private static extern UInt16 GetProductId(Int32 deviceId);

		/**
				 * 機器名を取得 (Get device name)
				 */
		[DllImport("unityuvcplugin", EntryPoint = "DeviceInfo_get_name"])
		[return: MarshalAs(UnmanagedType.LPStr)]
		private static extern string GetName(Int32 deviceId);


		/**
			 * デバイスまたはインターフェースが指定した条件に一致するかどうかを確認 (Check if device or interface matches specified conditions)
			 * @param device
			 * @param bClass 0xff なら常にマッチする (ワイルドカード) (0xff always matches (wildcard))
			 * @param bSubClass 0xff なら常にマッチする (ワイルドカード) (0xff always matches (wildcard))
			 * @param bProtocol 0xff なら常にマッチする (ワイルドカード) (0xff always matches (wildcard))
			 * @returtn 1: 一致した，0: 一致しなかった (1: matched, 0: not matched)
			 */
		[DllImport("unityuvcplugin", EntryPoint = "DeviceInfo_match"])
		private static extern Int32 InternalMatch(Int32 deviceId, byte bClass, byte bSubClass, byte bProtocol);
	} // UVCDevice

} // namespace Serenegiant.UVC


