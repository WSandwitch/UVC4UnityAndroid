//#define ENABLE_LOG
/*
 * Copyright (c) 2014 - 2022 t_saki@serenegiant.com
 */

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Serenegiant.UVC
{

	public class UVCDrawer : MonoBehaviour, IUVCDrawer
	{
		/**
			 * IUVCSelector がセットされていないとき
			 * または IUVCSelector が解像度選択時に null を
			 * 返したときのデフォルトの解像度 (幅)
			 * (Default resolution (width) when IUVCSelector is not set
			 * or IUVCSelector returns null during resolution selection)
			 */
		public int DefaultWidth = 1280;
		/**
			 * IUVCSelector がセットされていないとき
			 * または IUVCSelector が解像度選択時に null を
			 * 返したときのデフォルトの解像度 (高さ)
			 * (Default resolution (height) when IUVCSelector is not set
			 * or IUVCSelector returns null during resolution selection)
			 */
		public int DefaultHeight = 720;
		/**
			 * 可能な場合に UAC から音声取得を行うかどうか
			 * (Whether to get audio from UAC when possible)
			 */
		public bool UACEnabled = false;
		/**
			 * 接続時及び描画時のフィルタ用
			 * (For filters during connection and rendering)
			 */
		public UVCFilter[] UVCFilters;

		/**
			 * UVC 機器からの映像の描画先 Material を保持している GameObject
			 * 設定していない場合はこのスクリプトを割当てたのと同じ GameObjec を使う。
			 * (GameObject holding Material for UVC video rendering target
			 * If not set, use same GameObject as this script is attached to.)
			 */
		public List<GameObject> RenderTargets;
		/**
			 * UVC 機器の UAC 機能で取得した音声を再生するために使用する AudioSource を保持する GameObject
			 * 設定していない場合はこのスクリプトを割当てたのと同じ GameObjec を使う。
			 * (GameObject holding AudioSource for UAC audio playback
			 * If not set, use same GameObject as this script is attached to.)
			 */
		public GameObject AudioTarget;

		//--------------------------------------------------------------------------------
		private const string TAG = "UVCDrawer#";

		/**
			 * UVC 機器からの映像の描画先 Material
			 * TargetGameObject から取得する
			 * 優先順位：
			 *	 TargetGameObject の Skybox
			 *	 > TargetGameObject の Renderer
			 *	 > TargetGameObject の RawImage
			 *	 > TargetGameObject の Material
			 * いずれの方法でも取得できなければ Start で UnityException を投げる
			 * (UVC video rendering target Material
			 * Obtained from TargetGameObject
			 * Priority:
			 *  TargetGameObject's Skybox
			 *  > TargetGameObject's Renderer
			 *  > TargetGameObject's RawImage
			 *  > TargetGameObject's Material
			 * Throws UnityException in Start if none can be obtained)
			 */
		private UnityEngine.Object[] TargetMaterials;
		/**
			 * オリジナルのテクスチャ
			 * UVC カメラ映像受け取り用テクスチャをセットする前に
			 * GetComponent<Renderer>().material.mainTexture に設定されていた値
			 * (Original texture
			 * Value set to GetComponent<Renderer>().material.mainTexture before setting UVC camera video texture)
			 */
		private Texture[] SavedTextures;

		private Quaternion[] quaternions;

		//================================================================================

		// Start is called before the first frame update
		void Start()
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}Start:");
#endif
			UpdateRenderTarget();

		}

	//		// Update is called once per frame
	//		void Update()
	//		{
	//		}

		//================================================================================

		/**
			 * UVC 機器が接続された
			 * IOnUVCAttachHandler の実装
			 * @param manager 呼び出し元の UVCManager
			 * @param device 対象となる UVC 機器の情報
			 * @return true: UVC 機器を使用する，false: UVC 機器を使用しない
			 * (UVC device connected
			 * IOnUVCAttachHandler implementation
			 * @param manager UVCManager caller
			 * @param device UVC device information
			 * @return true: use UVC device, false: don't use UVC device)
			 */
		public bool OnUVCAttachEvent(UVCManager manager, UVCDevice device)
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}OnUVCAttachEvent:{device}");
#endif
			// XXX 今の実装では基本的に全ての UVC 機器を受け入れる (Current implementation accepts all UVC devices)
			// ただし THETA S と THETA V と THETA Z1 は映像を取得できないインターフェースがあるのでオミットする (THETA S/V/Z1 are omitted as they have interfaces that can't get video)
			// IsUVCEnabled と同様に UVC 機器フィルターをインスペクタで設定できるようにする (Can set UVC device filter in inspector like IsUVCEnabled)
			// (Current implementation basically accepts all UVC devices
			// THETA S/V/Z1 are omitted as they have interfaces that can't get video
			 // Can set UVC device filter in inspector like IsUVCEnabled)
			var result = !device.IsRicoh || device.IsTHETA;

			result &= UVCFilter.Match(device, UVCFilters, manager);

			return result;
		}

		/**
			 * UVC 機器が取り外された
			 * IOnUVCDetachEventHandler の実装
			 * @param manager 呼び出し元の UVCManager
			 * @param device 対象となる UVC 機器の情報
			 * (UVC device detached
			 * IOnUVCDetachEventHandler implementation
			 * @param manager UVCManager caller
			 * @param device UVC device information)
			 */
		public void OnUVCDetachEvent(UVCManager manager, UVCDevice device)
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}OnUVCDetachEvent:{device}");
#endif
		}

		/**
			 * IUVCDrawer が指定した UVC 機器の映像を描画できるかどうかを取得
			 * IUVCDrawer の実装
			 * @param manager 呼び出し元の UVCManager
			 * @param device 対象となる UVC 機器の情報
			 * (Get whether IUVCDrawer can render specified UVC device video
			 * IUVCDrawer implementation
			 * @param manager UVCManager caller
			 * @param device UVC device information)
			 */
		public bool IsUVCEnabled(UVCManager manager, UVCDevice device)
		{
			return UVCFilter.Match(device, UVCFilters, manager);
		}

		/**
			 * 映像取得を開始した
			 * IUVCDrawer の実装
			 * @param manager 呼び出し元の UVCManager
			 * @param device 対象となる UVC 機器の情報
			 * @param tex UVC 機器からの映像を受け取る Texture インスタンス
			 * (Video capture started
			 * IUVCDrawer implementation
			 * @param manager UVCManager caller
			 * @param device UVC device information
			 * @param tex Texture instance to receive video from UVC device)
			 */
		public void OnUVCStartEvent(UVCManager manager, UVCDevice device, Texture tex)
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}OnUVCStartEvent:{device}");
#endif
			HandleOnStartPreview(tex);
		}

		/**
			 * 映像取得を終了した
			 * IUVCDrawer の実装
			 * @param manager 呼び出し元の UVCManager
			 * @param device 対象となる UVC 機器の情報
			 * (Video capture ended
			 * IUVCDrawer implementation
			 * @param manager UVCManager caller
			 * @param device UVC device information)
			 */
		public void OnUVCStopEvent(UVCManager manager, UVCDevice device)
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}OnUVCStopEvent:{device}");
#endif
			HandleOnStopPreview();
		}

		/**
			 * IUVCDrawer が指定した UAC 機器 k からの音声を取得を有効にするかどうか取得
			 * XXX とりあえず UAC に対応した機器であれば true を返す，必要に応じて書き換えること
			 * IUVCDrawer の実装
			 * @param manager 呼び出し元の UVCManager
			 * @param device 対象となる UAC 機器の情報
			 * (Get whether IUVCDrawer enables audio capture from specified UAC device
			 * XXX For now return true if UAC-compatible device, change as needed
			 * IUVCDrawer implementation
			 * @param manager UVCManager caller
			 * @param device UAC device information)
			 */
		public bool IsUACEnabled(UVCManager manager, UVCDevice device)
		{
			return UACEnabled && device.isUAC;
		}

		/**
			 * UAC 機器からの音声取得を開始した
			 * @param manager 呼び出し元の UVCManager
			 * @param device 接続された UVC 機器情報
			 * @param audioClip UAC 機器からの音声を受け取る AudioClip オブジェクト
			 * (UAC audio capture started
			 * @param manager UVCManager caller
			 * @param device Connected UVC device info
			 * @param audioClip AudioClip object to receive audio from UAC device)
			 */
		public void OnUACStartEvent(UVCManager manager, UVCDevice device, AudioClip audioClip)
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}OnUACStartEvent:{device}");
#endif
			HandleOnStartAudio(audioClip);
		}

		/**
			 * UAC 機器からの音声取得を終了した
			 * @param manager 呼び出し元の UVCManager
			 * @param device 接続された UVC 機器情報
			 * (UAC audio capture ended
			 * @param manager UVCManager caller
			 * @param device Connected UVC device info)
			 */
		public void OnUACStopEvent(UVCManager manager, UVCDevice device)
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}OnUACStopEvent:{device}");
#endif
			HandleOnStopAudio();
		}

		//================================================================================
		/**
			 * 描画先を更新
			 * (Update render target)
			 */
		private void UpdateRenderTarget()
		{
			bool found = false;
			if ((RenderTargets != null) && (RenderTargets.Count > 0))
			{
				TargetMaterials = new UnityEngine.Object[RenderTargets.Count];
				SavedTextures = new Texture[RenderTargets.Count];
				quaternions = new Quaternion[RenderTargets.Count];
				int i = 0;
				foreach (var target in RenderTargets)
				{
					if (target != null)
					{
						var material = TargetMaterials[i] = GetTargetMaterial(target);
						if (material != null)
						{
							found = true;
						}
#if (!NDEBUG && DEBUG && ENABLE_LOG)
						Console.WriteLine($"{TAG}UpdateRenderTarget:material={material}");
#endif
					}
					i++;
				}
			}
			if (!found)
			{		// If no render target found, try to get from GameObject this script is attached to
				// (Should set gameObject in RenderTargets?)
				TargetMaterials = new UnityEngine.Object[1];
				SavedTextures = new Texture[1];
				quaternions = new Quaternion[1];
				TargetMaterials[0] = GetTargetMaterial(gameObject);
				found = TargetMaterials[0] != null;
			}

			if (!found)
			{
				throw new UnityException("no target material found.");
			}
		}

		/**
			 * テクスチャとして映像を描画する Material を取得する
			 * 指定した GameObject に Skybox/Renderer/RawImage/Material があればそれから Material を取得する
			 * それぞれが複数割り当てられている場合最初に見つかった使用可能ものを返す
			 * 優先度：Skybox > Renderer > RawImage > Material
			 * @param target
			 * @return 見つからなければ null を返す
			 * (Get Material to render video as texture
			 * Get Material from Skybox/Renderer/RawImage/Material if exists on specified GameObject
			 * Return first usable one if multiple assigned
			 * Priority: Skybox > Renderer > RawImage > Material
			 * @param target
			 * @return Return null if not found)
			 */
		UnityEngine.Object GetTargetMaterial(GameObject target/*NonNull*/)
		{
			// Skybox の取得を試みる (Try to get Skybox)
			var skyboxs = target.GetComponents<Skybox>();
			if (skyboxs != null)
			{
				foreach (var skybox in skyboxs)
				{
					if (skybox.isActiveAndEnabled && (skybox.material != null))
					{
						RenderSettings.skybox = skybox.material;
						return skybox.material;
					}
				}
			}
			// Skybox が取得できなければ Renderer の取得を試みる (If Skybox not found, try Renderer)
			var renderers = target.GetComponents<Renderer>();
			if (renderers != null)
			{
				foreach (var renderer in renderers)
				{
					if (renderer.enabled && (renderer.material != null))
					{
						return renderer.material;
					}

				}
			}
			// Skybox も Renderer も取得できなければ RawImage の取得を試みる (If both Skybox and Renderer not found, try RawImage)
			var rawImages = target.GetComponents<RawImage>();
			if (rawImages != null)
			{
				foreach (var rawImage in rawImages)
				{
					if (rawImage.enabled && (rawImage.material != null))
					{
						return rawImage;
					}

				}
			}
			// Skybox も Renderer も RawImage も取得できなければ Material の取得を試みる (If all Skybox, Renderer, RawImage not found, try Material)
			var material = target.GetComponent<Material>();
			if (material != null)
			{
				return material;
			}
			return null;
		}

		private void RestoreTexture()
		{
			for (int i = 0; i < TargetMaterials.Length; i++)
			{
				var target = TargetMaterials[i];
				try
				{
					if (target is Material)
					{
						(target as Material).mainTexture = SavedTextures[i];
					}
					else if (target is RawImage)
					{
						(target as RawImage).texture = SavedTextures[i];
					}
				}
				catch
				{
					Console.WriteLine($"{TAG}RestoreTexture:Exception cought");
				}
				SavedTextures[i] = null;
				quaternions[i] = Quaternion.identity;
			}
		}

		private void ClearTextures()
		{
			for (int i = 0; i < SavedTextures.Length; i++)
			{
				SavedTextures[i] = null;
			}
		}

		/**
			 * 映像取得開始時の処理
			 * @param tex 映像を受け取るテクスチャ
			 * (Processing when video capture starts
			 * @param tex Texture to receive video)
			 */
		private void HandleOnStartPreview(Texture tex)
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}HandleOnStartPreview:({tex})");
#endif
			int i = 0;
			foreach (var target in TargetMaterials)
			{
				if (target is Material)
				{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
					Console.WriteLine($"{TAG}HandleOnStartPreview:assign Texture to Material({target})");
#endif
					SavedTextures[i++] = (target as Material).mainTexture;
					(target as Material).mainTexture = tex;
				}
				else if (target is RawImage)
				{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
					Console.WriteLine($"{TAG}HandleOnStartPreview:assign Texture to RawImage({target})");
#endif
					SavedTextures[i++] = (target as RawImage).texture;
					(target as RawImage).texture = tex;
				}
			}
		}

		/**
			 * 映像取得が終了したときの Unity 側の処理
			 * (Unity-side processing when video capture ends)
			 */
		private void HandleOnStopPreview()
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}HandleOnStopPreview:");
#endif
			// 描画先のテクスチャをもとに戻す (Restore render target texture)
			RestoreTexture();
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}HandleOnStopPreview:finished");
#endif
		}

		/**
			 * UAC の音声再生を行う AudioSource を取得する
			 * (Get AudioSource for UAC audio playback)
			 */
		private AudioSource GetAudioSource()
		{
			AudioSource result = null;
			if (AudioTarget != null)
			{
				result = AudioTarget.GetComponent<AudioSource>();
			}
			if (result == null)
			{
				result = GetComponent<AudioSource>();
			}

#if (!NDEBUG && DEBUG && ENABLE_LOG)
			if (result == null)
			{
				Console.WriteLine($"{TAG}GetAudioSource:audio source not found");
			}
#endif
			return result;
		}

		/**
			 * 音声取得開始した時の Unity 側の処理
			 * @param audioClip
			 * (Unity-side processing when audio capture starts
			 * @param audioClip)
			 */
		private void HandleOnStartAudio(AudioClip audioClip)
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}HandleOnStartAudio:");
#endif
			var audioSource = GetAudioSource();
			if (audioSource != null)
			{
				audioSource.Stop();
				audioSource.clip = audioClip;
				audioSource.Play();
			}
		}

		/**
			 * 音声取得終了した時の Unity 側の処理
			 * (Unity-side processing when audio capture ends)
			 */
		private void HandleOnStopAudio()
		{
#if (!NDEBUG && DEBUG && ENABLE_LOG)
			Console.WriteLine($"{TAG}HandleOnStopAudio:");
#endif
			var audioSource = GetAudioSource();
			if (audioSource != null)
			{
				audioSource.Stop();
				audioSource.clip = null;
			}
		}

	} // class UVCDrawer

} // namespace Serenegiant.UVC
