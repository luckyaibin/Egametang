﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Base;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Model
{
	[EntityEvent(EntityEventId.ResourcesComponent)]
	public class ResourcesComponent: Component
	{
		public static AssetBundleManifest AssetBundleManifestObject { get; set; }

		private readonly Dictionary<string, UnityEngine.Object> resourceCache = new Dictionary<string, UnityEngine.Object>();

		private readonly Dictionary<string, AssetBundle> bundleCaches = new Dictionary<string, AssetBundle>();
		
		public K GetReference<K>(string bundle, string prefab, string key) where K : class
		{
			GameObject gameObject = this.GetAsset<GameObject>(bundle, prefab);
			return gameObject.GetComponent<ReferenceCollector>().Get<K>(key);
		}

		public K GetAsset<K>(string bundleName, string prefab) where K : class
		{
			string path = $"{bundleName}.unity3d/{prefab}".ToLower();

			UnityEngine.Object resource = null;
			if (this.resourceCache.TryGetValue(path, out resource))
			{
				return resource as K;
			}

			if (Define.LoadResourceType == LoadResourceType.Async)
			{
				if (!this.bundleCaches.ContainsKey($"{bundleName}.unity3d".ToLower()))
				{
					return null;
				}

				throw new ConfigException($"异步加载资源,资源不在resourceCache中: {bundleName} {path}");
			}

			try
			{
#if UNITY_EDITOR
				string[] realPath = AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(bundleName.ToLower() + ".unity3d", prefab);
				resource = AssetDatabase.LoadAssetAtPath(realPath[0], typeof (GameObject));
				this.resourceCache.Add(path, resource);
#endif
			}
			catch (Exception e)
			{
				throw new ConfigException($"加载资源出错,输入路径:{path}", e);
			}

			return resource as K;
		}

		public async Task DownloadAndCacheAsync(string uri, string assetBundleName)
		{
			assetBundleName = (assetBundleName + ".unity3d").ToLower();

			AssetBundle assetBundle;
			// 异步下载资源
			string url = uri + "StreamingAssets/" + assetBundleName;
			int count = 0;
			while (true)
			{
				try
				{
					++count;
					if (count > 1)
					{
						await Game.Scene.GetComponent<TimerComponent>().WaitAsync(2000);
					}

					if (this.Id == 0)
					{
						return;
					}

					using (WWWAsync wwwAsync = new WWWAsync())
					{
						await wwwAsync.LoadFromCacheOrDownload(url, ResourcesComponent.AssetBundleManifestObject.GetAssetBundleHash(assetBundleName));
						assetBundle = wwwAsync.www.assetBundle;
					}
					break;
				}
				catch (Exception e)
				{
					Log.Error(e.ToString());
				}
			}

			if (!assetBundle.isStreamedSceneAssetBundle)
			{
				// 异步load资源到内存cache住
				UnityEngine.Object[] assets;
				using (AssetBundleLoaderAsync assetBundleLoaderAsync = new AssetBundleLoaderAsync(assetBundle))
				{
					assets = await assetBundleLoaderAsync.LoadAllAssetsAsync();
				}

				foreach (UnityEngine.Object asset in assets)
				{
					string path = $"{assetBundleName}/{asset.name}".ToLower();
					this.resourceCache[path] = asset;
				}
			}

			if (this.bundleCaches.ContainsKey(assetBundleName))
			{
				throw new GameException($"重复加载资源: {assetBundleName}");
			}
			this.bundleCaches[assetBundleName] = assetBundle;
		}

		public override void Dispose()
		{
			if (this.Id == 0)
			{
				return;
			}

			base.Dispose();

			foreach (var assetBundle in bundleCaches)
			{
				assetBundle.Value?.Unload(true);
			}
		}
	}
}