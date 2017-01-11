﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WzComparerR2.WzLib;
using WzComparerR2.PluginBase;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using WzComparerR2.Animation;
using WzComparerR2.Rendering;
using WzComparerR2.Common;
using Spine;

namespace WzComparerR2.MapRender
{
    public class ResourceLoader : IDisposable
    {
        public ResourceLoader(IServiceProvider serviceProvider)
        {
            this.Services = serviceProvider;
            this.loadedItems = new Dictionary<string, ResourceHolder>();
            this.loadedAnimationData = new Dictionary<string, object>();
        }

        public IServiceProvider Services { get; protected set; }

        protected Dictionary<string, ResourceHolder> loadedItems;
        protected Dictionary<string, object> loadedAnimationData;
        private bool isCounting;

        private GraphicsDevice GraphicsDevice
        {
            get
            {
                return ((IGraphicsDeviceService)this.Services.GetService(typeof(IGraphicsDeviceService))).GraphicsDevice;
            }
        }

        public virtual T Load<T>(string assetName)
        {
            return Load<T>(assetName: assetName);
        }

        private T Load<T>(Wz_Node node = null, string assetName = null)
        {
            if (node == null && assetName == null)
                throw new ArgumentNullException();

            assetName = assetName ?? node.FullPathToFile;

            ResourceHolder holder;
            if (!loadedItems.TryGetValue(assetName, out holder))
            {
                var res = InnerLoad(node ?? PluginManager.FindWz(assetName), typeof(T));
                if (res == null)
                {
                    return default(T);
                }

                holder = new ResourceHolder();
                holder.Resource = res;
                loadedItems[assetName] = holder;
            }

            //结算计数器
            if (isCounting)
            {
                holder.Count++;
            }

            //特殊处理
            return (T)holder.Resource;
        }

        public object LoadAnimationData(string assetName)
        {
            object aniData;
            if (!loadedAnimationData.TryGetValue(assetName, out aniData))
            {
                aniData = InnerLoadAnimationData(assetName);
                if (aniData == null)
                {
                    return null;
                }
                loadedAnimationData[assetName] = aniData;
            }
            return aniData;
        }

        public void BeginCounting()
        {
            foreach(var kv in this.loadedItems)
            {
                kv.Value.Count = 0;
            }
            isCounting = true;
        }

        public void EndCounting()
        {
            isCounting = false;
        }

        public void Unload()
        {
            foreach(var kv in this.loadedItems)
            {
                var res = kv.Value.Resource;
                if (res is Texture2D)
                {
                    ((Texture2D)res).Dispose();
                }
                else if (res is TextureAtlas) //回头再考虑回收策略
                {
                    ((TextureAtlas)res).Texture.Dispose();
                }
            }

            this.loadedItems.Clear();
            this.loadedAnimationData.Clear();
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        private object InnerLoad(Wz_Node node, Type assetType)
        {
            if (assetType == typeof(TextureAtlas)) //回头再说
            {
                return new TextureAtlas(((Wz_Png)node.Value).ToTexture(this.GraphicsDevice));
            }
            else if (assetType == typeof(Texture2D))
            {
                return ((Wz_Png)node.Value).ToTexture(this.GraphicsDevice);
            }
            return null;
        }

        private object InnerLoadAnimationData(string assetName)
        {
            var node = PluginManager.FindWz(assetName);
            if (node != null)
            {
                if (node.Value is Wz_Uol)
                {
                    node = ((Wz_Uol)node.Value).HandleUol(node);
                }

                if (node.Value is Wz_Png) //单帧动画
                {
                    var aniData = new FrameAnimationData();
                    var frame = LoadFrame(node);
                    aniData.Frames.Add(frame);
                    return aniData;
                }
                else if (node.Value == null && node.Nodes.Count > 0) //分析目录
                {
                    string spine = node.Nodes["spine"].GetValueEx<string>(null);
                    if (spine != null) //读取spine动画
                    {
                        var loader = new SpineTextureLoader(this, node);
                        var atlasNode = node.Nodes[spine + ".atlas"];
                        var aniData = SpineAnimationData.CreateFromNode(atlasNode, null, loader);
                        return aniData;
                    }
                    else //读取序列帧动画
                    {
                        var frames = new List<Frame>();
                        Wz_Node frameNode;
                        for (int i = 0; (frameNode = node.Nodes[i.ToString()]) != null; i++)
                        {
                            var frame = LoadFrame(frameNode);
                            frames.Add(frame);
                        }
                        var repeat = node.Nodes["repeat"].GetValueEx<int>(0);
                        if (repeat <= 0)
                        {
                            return new FrameAnimationData(frames);
                        }
                        else
                        {
                            return new RepeatableFrameAnimationData(frames) { Repeat = repeat };
                        }
                    }
                }
            }
            return null;
        }

        private Frame LoadFrame(Wz_Node node)
        {
            //处理uol
            if (node.Value is Wz_Uol)
            {
                node = ((Wz_Uol)node.Value).HandleUol(node);
            }
            //寻找link
            var linkNode = node.GetLinkedSourceNode(PluginManager.FindWz);
            //加载资源
            var atlas = Load<TextureAtlas>(linkNode);
            //读取其他信息
            var frame = new Frame()
            {
                Texture = atlas.Texture,
                AtlasRect = atlas.SrcRect,
                A0 = linkNode.Nodes["a0"].GetValueEx(255),
                A1 = linkNode.Nodes["a1"].GetValueEx(255),
                Z = linkNode.Nodes["z"].GetValueEx(0),
                Delay = linkNode.Nodes["delay"].GetValueEx(100),
                Origin = (linkNode.Nodes["origin"]?.Value as Wz_Vector)?.ToPoint() ?? Point.Zero
            };
            return frame;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Unload();
            }
        }

        ~ResourceLoader()
        {
            this.Dispose(false);
        }

        protected class ResourceHolder
        {
            public object Resource { get; set; }
            public int Count { get; set; }
        }

        private class SpineTextureLoader : Spine.TextureLoader
        {
            public SpineTextureLoader(ResourceLoader resLoader, Wz_Node topNode)
            {
                this.BaseLoader = resLoader;
                this.TopNode = topNode;
            }

            public ResourceLoader BaseLoader { get; set; }
            public Wz_Node TopNode { get; set; }

            public void Load(AtlasPage page, string path)
            {
                var frameNode = this.TopNode.FindNodeByPath(path);

                if (frameNode == null || frameNode.Value == null)
                {
                    return;
                }

                //处理uol
                if (frameNode.Value is Wz_Uol)
                {
                    frameNode = ((Wz_Uol)frameNode.Value).HandleUol(frameNode);
                }
                //寻找link
                var linkNode = frameNode.GetLinkedSourceNode(PluginManager.FindWz);
                //加载资源
                var texture = BaseLoader.Load<Texture2D>(linkNode);

                page.rendererObject = texture;
                page.width = texture.Width;
                page.height = texture.Height;
            }

            public void Unload(object texture)
            {
                //什么都不做
            }
        }
    }
}