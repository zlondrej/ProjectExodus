﻿using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace SceneExport{
	public static class TextureUtility{	
		static readonly string[] supportedTexExtensions = new string[]{".bmp", ".float", ".pcx", ".png", 
			".psd", ".tga", ".jpg", ".jpeg", ".exr", ".dds", ".hdr"};
		
		public static bool isSupportedTexExtension(string ext){
			return supportedTexExtensions.Contains(ext.ToLower());
		}
		
		static void destroyEditorObject(Object obj){
			if (Application.isEditor && !Application.isPlaying)
				Object.DestroyImmediate(obj);
			else
				Object.Destroy(obj);
		}
		
		public class EditorObjectGuard<T>: System.IDisposable where T: Object{
			public T obj;
			public void Dispose(){
				destroyEditorObject(obj);
			}
			public EditorObjectGuard(T obj_){
				obj = obj_;
			}
		}
		
		public static EditorObjectGuard<T> makeGuard<T>(T obj) where T: Object{
			return new EditorObjectGuard<T>(obj);
		}
		
		/*
		Warning: Uses unity texture creation functions
		*/
		public static void saveRawColorsToPng(string filePath, int width, int height, Color[] colors, bool linear){
			var fmt = TextureFormat.ARGB32;;
			using(var guard = makeGuard(new Texture2D(width, height, fmt, false, linear))){
				var tex = guard.obj;
				tex.SetPixels(colors);
				tex.Apply();
				var bytes = tex.EncodeToPNG();
				System.IO.File.WriteAllBytes(filePath, bytes);
			}
		}
		
		public static readonly string unsupportedTexFolder = "!Unsupported";
		
		static void swap<T>(ref T arg1, ref T arg2){
			T tmp = arg1;
			arg1 = arg2;
			arg2 = tmp;
		}
		
		static void flipY(int width, int height, Color[] colors){
			for(int y = 0; y < height/2; y++){
				var srcStart = y * height;
				var dstStart = (height - y - 1) * height;
				for(int x = 0; x < width; x++){
					var srcIndex = srcStart + x;
					var dstIndex = dstStart + x;
					
					var tmp = colors[srcIndex];
					colors[srcIndex] = colors[dstIndex];
					colors[dstIndex] = tmp;
				}
			}
		}
		
		public static void processCubemapFace(CubemapFace cubeFace, JsonCubemap jsonCube, System.Action<Texture2D, bool> texCallback){
			if (texCallback == null){
				throw new System.ArgumentNullException("texCallback");
			}
			Texture2D tmpTex = null;
			Texture2D rgbTex = null;
			RenderTexture rendTex = null;
			RenderTexture prevRenderTex = RenderTexture.active;
			try{			
				var cubeSize = jsonCube.texParams.width;
				bool linear = !jsonCube.texImportParams.importer.sRGBTexture;
				bool hasMips = jsonCube.cubemap.mipmapCount > 1;
				tmpTex  = new Texture2D(cubeSize, cubeSize, jsonCube.cubemap.format, hasMips, linear);				
				Graphics.CopyTexture(jsonCube.cubemap, (int)cubeFace, tmpTex, 0);
				tmpTex.Apply();

				bool hdr = jsonCube.isHdr;
				
				var texFmt = hdr ? TextureFormat.RGBAFloat: TextureFormat.RGBA32;
				rgbTex = new Texture2D(cubeSize, cubeSize, texFmt, false, linear);
				
				rendTex = RenderTexture.GetTemporary(cubeSize, cubeSize, 0, 
					hdr ? RenderTextureFormat.ARGBFloat: RenderTextureFormat.ARGB32, 
					linear ? RenderTextureReadWrite.Linear: RenderTextureReadWrite.sRGB);
					
				Graphics.Blit(tmpTex, rendTex);
				rgbTex.ReadPixels(new Rect(0, 0, rendTex.width, rendTex.height), 0, 0);
				rgbTex.Apply();
				
				texCallback(rgbTex, hdr);
				/*
				var result = rgbTex.GetPixels();
				flipY(cubeSize, cubeSize, result);
				return result;
				*/
			}
			finally{
				if (rendTex){
					RenderTexture.active = prevRenderTex;
					RenderTexture.ReleaseTemporary(rendTex);					
				}
				destroyEditorObject(tmpTex);
				destroyEditorObject(rgbTex);
			}
		}
		
		public static bool isHdrFormat(TextureFormat fmt){
			return (fmt == TextureFormat.RGBAFloat) 
				|| (fmt == TextureFormat.RGBAHalf)
				|| (fmt == TextureFormat.BC6H);
		}
		
		public static byte[] getCubemapFaceRawData(CubemapFace cubeFace, JsonCubemap jsonCube){
			byte[] rawData = null;
			processCubemapFace(cubeFace, jsonCube, (tex, isHdr) => {
				rawData = tex.GetRawTextureData();
				//result = tex.GetPixels();
			});
			
			return rawData;
		}
		
		public class SquareTransformDesc{
			Vector2Int origin;
			Vector2Int up;
			Vector2Int right;
			
			static int getPixOffset(Vector2Int coords, int size){
				var height = size;
				//var width = size;
				return coords.y * height + coords.x;
			}
			
			public void getScanParameters(int width, out int startOffset, out int pixelAdd, out int scanAdd){
				//var height = width;
				var maxOffset = width - 1;
				startOffset = getPixOffset(origin * maxOffset, width);
				var xPlusOffset = getPixOffset(right * maxOffset, width);
				var yPlusOffset = getPixOffset(up * maxOffset, width);
				pixelAdd = (xPlusOffset - startOffset)/maxOffset;
				scanAdd = (yPlusOffset - startOffset)/maxOffset;
			}
			
			public SquareTransformDesc(Vector2Int origin_, Vector2Int up_, Vector2Int right_){
				origin = origin_;
				up = up_;
				right = right_;
			}
		};
		
		public enum CubemapRotation{
			None, cw90, cw180, cw270, flipX, flipY
		};
		
		public static T[] transformSquareArray<T>(T[] arg, int width, SquareTransformDesc desc){
			if (desc == null)
				return arg;
				
			var height = width;
			int srcBase = 0;
			int srcScanAdd = width;
			int srcPixelAdd = 1;
			
			desc.getScanParameters(width, out srcBase, out srcPixelAdd, out srcScanAdd);
						
			var result = new T[arg.Length];
			int dstBase = 0;
			int dstScanLine = dstBase;
			var srcScanLine = srcBase;
			
			for(int y = 0; y < height; y++){
				var dstPixel = dstScanLine;
				var srcPixel = srcScanLine;
				for(int x = 0; x < width; x++){
					result[dstPixel] = arg[srcPixel];
					srcPixel += srcPixelAdd;
					dstPixel++;
				}
				srcScanLine += srcScanAdd;
				dstScanLine += width;
			}
			
			return result;
		}
		
		public static Color[] getCubemapFace(CubemapFace cubeFace, JsonCubemap jsonCube){
			if (jsonCube.cubemap && jsonCube.texImportParams.initialized && jsonCube.texImportParams.importer.isReadable){
				return jsonCube.cubemap.GetPixels(cubeFace);
			}

			Color[] result = null;
			processCubemapFace(cubeFace, jsonCube, (tex, isHdr) => {
				result = tex.GetPixels();
			});
			
			return result;
		}
		
		public static Color32[] getCubemapFace32(CubemapFace cubeFace, JsonCubemap jsonCube){
			Color32[] result = null;
			processCubemapFace(cubeFace, jsonCube, (tex, isHdr) => {
				result = tex.GetPixels32();
			});
			
			return result;
		}
		
		public static void writeColorArray(System.IO.BinaryWriter writer, Color[] colors){
			foreach(var cur in colors){
				writer.Write(cur.b);//to comply with unreal data format.
				writer.Write(cur.g);
				writer.Write(cur.r);
				writer.Write(cur.a);
			}
		}
		
		public static void writeColorArray(System.IO.BinaryWriter writer, Color32[] colors){
			foreach(var cur in colors){
				writer.Write(cur.b);
				writer.Write(cur.g);
				writer.Write(cur.r);
				writer.Write(cur.a);
			}
		}
		
		public static void saveRawCubemap(JsonCubemap jsonCube, string filename, Logger logger = null){
			Logger.makeValid(ref logger);
			var cubeSize = jsonCube.texParams.width;
			//bool rgb = jsonCube.
			
			var hdr = jsonCube.isHdr;
			//compression?
			using(var writer = new System.IO.BinaryWriter(
				System.IO.File.Open(filename, System.IO.FileMode.Create))){
				
				var faces = new List<CubemapFace>(){
					CubemapFace.PositiveZ,
					CubemapFace.NegativeZ,
					CubemapFace.PositiveX,
					CubemapFace.NegativeX,
					CubemapFace.PositiveY,
					CubemapFace.NegativeY
				};
				
				var rotations = new List<SquareTransformDesc>(){
					new SquareTransformDesc(new Vector2Int(1, 0), new Vector2Int(0, 0), new Vector2Int(1, 1)),
					new SquareTransformDesc(new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(0, 0)),
					new SquareTransformDesc(new Vector2Int(1, 1), new Vector2Int(1, 0), new Vector2Int(0, 1)),
					new SquareTransformDesc(new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 0)),
					new SquareTransformDesc(new Vector2Int(1, 0), new Vector2Int(0, 0), new Vector2Int(1, 1)),
					new SquareTransformDesc(new Vector2Int(1, 0), new Vector2Int(0, 0), new Vector2Int(1, 1))
				};
				
				for(int i = 0; i < faces.Count; i++){
					var curFace = faces[i];
					var curRotation = rotations[i];
					if (hdr){
						var data = getCubemapFace(curFace, jsonCube);						
						data = transformSquareArray(data, cubeSize, curRotation);
						writeColorArray(writer, data);
					}
					else{
						var data = getCubemapFace32(curFace, jsonCube);
						data = transformSquareArray(data, cubeSize, curRotation);
						writeColorArray(writer, data);
					}
				}
			}			
		}
		
		public static void saveHorizontalStripCubemap(JsonCubemap jsonCube, string targetPath){
			var cubeSize = jsonCube.texParams.width;
			
			var texWidth = cubeSize;
			var texHeight = cubeSize;
			texWidth = texWidth * 6;
			
			var xPos = getCubemapFace(CubemapFace.PositiveX, jsonCube);
			var xNeg = getCubemapFace(CubemapFace.NegativeX, jsonCube);
			var yPos = getCubemapFace(CubemapFace.PositiveY, jsonCube);
			var yNeg = getCubemapFace(CubemapFace.NegativeY, jsonCube);
			var zPos = getCubemapFace(CubemapFace.PositiveZ, jsonCube);
			var zNeg = getCubemapFace(CubemapFace.NegativeZ, jsonCube);
			
			var texFmt = TextureFormat.RGBA32;
			
			var newTex = new Texture2D(texWidth, texHeight, texFmt, false);
			
			newTex.SetPixels(0, 0, cubeSize, cubeSize, xPos, 0);
			newTex.SetPixels(cubeSize * 1, 0, cubeSize, cubeSize, xNeg, 0);
			newTex.SetPixels(cubeSize * 2, 0, cubeSize, cubeSize, yPos, 0);
			newTex.SetPixels(cubeSize * 3, 0, cubeSize, cubeSize, yNeg, 0);
			newTex.SetPixels(cubeSize * 4, 0, cubeSize, cubeSize, zPos, 0);
			newTex.SetPixels(cubeSize * 5, 0, cubeSize, cubeSize, zNeg, 0);
			newTex.Apply();
			
			var pngBytes = newTex.EncodeToPNG();
			
			
			destroyEditorObject(newTex);
						
			System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath));
			Utility.saveBytesToFile(targetPath, pngBytes);
		}
		
		public static void copyCubemap(JsonCubemap jsonCube, string targetDir, string projectDir, Logger logger = null){
			Logger.makeValid(ref logger);
			logger = Logger.getValid(logger);
			
			if (!jsonCube.texParams.initialized){
				logger.logErrorFormat("Cubemap {0} is not initialzied", jsonCube.name);
				return;
			}
			
			var targetPath = System.IO.Path.Combine(targetDir, jsonCube.exportPath);
			var unsupportedDir = System.IO.Path.Combine(targetDir, unsupportedTexFolder);
			var unsupportedPath = System.IO.Path.Combine(unsupportedDir, jsonCube.assetPath);
			System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetPath));
			var srcPath = System.IO.Path.Combine(projectDir, jsonCube.assetPath);
			
			if (!jsonCube.needConversion){
				System.IO.File.Copy(srcPath, targetPath, true);
				return;
			}

			System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(unsupportedPath));
			System.IO.File.Copy(srcPath, unsupportedPath, true);
			
			logger.logWarningFormat("Cubemap \"{0}\"({1}) requires conversion. Data loss possible", jsonCube.name, jsonCube.id);

			saveHorizontalStripCubemap(jsonCube, targetPath);
			
			if (!string.IsNullOrEmpty(jsonCube.rawPath)){
				var targetRaw = System.IO.Path.Combine(targetDir, jsonCube.rawPath);
				saveRawCubemap(jsonCube, targetRaw);
			}
			/*
			jsonCube.cubemap.GetPixels(CubemapFace.
			
			Graphics.CopyTexture(*/
		}
		
		public static void copyTexture(JsonTexture jsonTex, string targetDir, string projectDir, Logger logger = null){
			logger = Logger.getValid(logger);
			var texPath = jsonTex.path;
			var ext = System.IO.Path.GetExtension(texPath).ToLower();
			var srcPath = System.IO.Path.Combine(projectDir, texPath);//TODO: The asset can be elswhere.
				
			bool supportedFile = isSupportedTexExtension(ext);
			if (!supportedFile){
				logger.logWarningFormat(
					"Import of \"{0}\" format used by \"{1}\" is not supported.\n" + 
					"Attempting conversion to \"*.png\"\n" + 
					"Target material will be remapped to use converted texture.", ext, texPath);
			}
			bool exists = System.IO.File.Exists(srcPath);
				
			var dstPath = System.IO.Path.Combine(targetDir, texPath);
			var dstDir = System.IO.Path.GetDirectoryName(dstPath);
			System.IO.Directory.CreateDirectory(dstDir);
				
			if (exists){
				if (supportedFile){
					System.IO.File.Copy(srcPath, dstPath, true);
					return;
				}
				else{
					var unsupportedPath = System.IO.Path.Combine(targetDir, unsupportedTexFolder);
					unsupportedPath = System.IO.Path.Combine(unsupportedPath, texPath);
					System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(unsupportedPath));
					//System.IO.File.Copy(srcPath, dstPath, true);					
					System.IO.File.Copy(srcPath, unsupportedPath, true);					
				}
			}
			else{
				logger.logWarningFormat("Asset \"{0}\" is not found on disk, attempting recovery from texture data.", srcPath);	
			}
				
			bool useExr = false;
			var formatExt = useExr ? ".exr" : ".png";
			logger.logWarningFormat("Attempting to write image data in {1} format for: {0}\nData Loss possible.", texPath, formatExt);
			var tex2D = (Texture2D)(jsonTex.textureRef);
			if (!tex2D){
				logger.logWarningFormat("Not a 2d texture: \"{0}\". Texture not saved.", texPath);
				return;
			}
				
			var savePath = System.IO.Path.ChangeExtension(dstPath, formatExt);
			
			TextureUtility.saveReadOnlyTexture(savePath, tex2D, jsonTex, useExr);
		}
	
		/*
		Have to do it the hard way, unless we implement tiff reader in C#.
		*/
		public static void saveReadOnlyTexture(string path, Texture2D tex, JsonTexture jsonTex, bool useExr, Logger logger = null){
			logger = Logger.getValid(logger);
			bool compressedNormalMap = jsonTex.normalMapFlag && 
				((tex.format == TextureFormat.DXT5) || (tex.format == TextureFormat.DXT1) || (tex.format == TextureFormat.BC5));
			var texFmt = TextureFormat.ARGB32;
			var rendFmt = RenderTextureFormat.ARGB32;
			bool linear = !jsonTex.sRGB;
			RenderTexture tmpRend = RenderTexture.GetTemporary(tex.width, tex.height, 0, 
				rendFmt, linear ? RenderTextureReadWrite.Linear: RenderTextureReadWrite.sRGB);
					
			Graphics.Blit(tex, tmpRend);
			RenderTexture prev = tmpRend;
			RenderTexture.active = tmpRend;
			Texture2D tmpTex2d = new Texture2D(tex.width, tex.height, texFmt, false, linear);
			tmpTex2d.hideFlags = HideFlags.HideAndDontSave;
			tmpTex2d.ReadPixels(new Rect(0, 0, tmpRend.width, tmpRend.height), 0, 0);
			tmpTex2d.Apply();
				
			logger.logFormat("Format of processed normalmap : {0}", tex.format);
			if (compressedNormalMap){
				logger.logWarningFormat("Compressed normalmap detected: \"{0}\" ({1}).\n" + 
					"Texture conversion will be performed.\n" + 
					"Data loss may occur while saving to png.", jsonTex.path, tex.format);
				var pixels = tmpTex2d.GetPixels();
				for(int i = 0; i < pixels.Length; i++){
					var packed = pixels[i];
					float x = packed.r * packed.a;
					float y = packed.g;
					x = x * 2.0f - 1.0f;
					y = y * 2.0f - 1.0f;
					float z = Mathf.Sqrt(1.0f - (x*x + y*y));
						
					pixels[i] = new Color(x*0.5f + 0.5f, y*0.5f + 0.5f, z*0.5f + 0.5f, 1.0f);
				}
				tmpTex2d.SetPixels(pixels);
				tmpTex2d.Apply();
			}
			RenderTexture.active = prev;
			RenderTexture.ReleaseTemporary(tmpRend);
				
			var bytes = useExr ? tmpTex2d.EncodeToEXR(): tmpTex2d.EncodeToPNG();
			destroyEditorObject(tmpTex2d);
			/*
			if (Application.isEditor && !Application.isPlaying)
				Object.DestroyImmediate(tmpTex2d);
			else
				Object.Destroy(tmpTex2d);
				*/
			Utility.saveBytesToFile(path, bytes);
		}
	}
}
