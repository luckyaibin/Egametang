﻿using System;
using System.Collections.Generic;
using Base;

namespace Model
{
	/// <summary>
	/// 消息分发组件
	/// </summary>
	[EntityEvent(EntityEventId.MessageDispatherComponent)]
	public class MessageDispatherComponent: Component
	{
		private AppType AppType;
		private Dictionary<ushort, List<IInstanceMethod>> handlers;
		private DoubleMap<ushort, Type> opcodeTypes = new DoubleMap<ushort, Type>();

		private void Awake(AppType appType)
		{
			this.AppType = appType;
			this.Load();
		}

		private void Load()
		{
			this.handlers = new Dictionary<ushort, List<IInstanceMethod>>();
			this.opcodeTypes = new DoubleMap<ushort, Type>();

			Type[] monoTypes = DllHelper.GetMonoTypes();
			foreach (Type monoType in monoTypes)
			{
				object[] attrs = monoType.GetCustomAttributes(typeof(MessageAttribute), false);
				if (attrs.Length == 0)
				{
					continue;
				}

				MessageAttribute messageAttribute = attrs[0] as MessageAttribute;
				if (messageAttribute == null)
				{
					continue;
				}

				this.opcodeTypes.Add(messageAttribute.Opcode, monoType);
			}
#if ILRuntime
			Type[] types = DllHelper.GetHotfixTypes();
#else
			Type[] types = DllHelper.GetMonoTypes();
#endif
			foreach (Type type in types)
			{
				object[] attrs = type.GetCustomAttributes(typeof(MessageHandlerAttribute), false);
				if (attrs.Length == 0)
				{
					continue;
				}
				MessageHandlerAttribute messageHandlerAttribute = (MessageHandlerAttribute)attrs[0];
#if ILRuntime
				IInstanceMethod method = new ILInstanceMethod(type, "Handle");
#else
				IInstanceMethod method = new MonoInstanceMethod(type, "Handle");
#endif
				if (!this.handlers.ContainsKey(messageHandlerAttribute.Opcode))
				{
					this.handlers.Add(messageHandlerAttribute.Opcode, new List<IInstanceMethod>());
				}
				this.handlers[messageHandlerAttribute.Opcode].Add(method);
			}
		}

		public ushort GetOpcode(Type type)
		{
			return this.opcodeTypes.GetKeyByValue(type);
		}

		public void Handle(Session session, MessageInfo messageInfo)
		{
			List<IInstanceMethod> actions;
			if (!this.handlers.TryGetValue(messageInfo.Opcode, out actions))
			{
				Log.Error($"消息 {messageInfo.Opcode} 没有处理");
				return;
			}

			Type messageType = this.opcodeTypes.GetValueByKey(messageInfo.Opcode);
			object message = MongoHelper.FromBson(messageType, messageInfo.MessageBytes, messageInfo.Offset, messageInfo.Count);
			messageInfo.Message = message;

			foreach (IInstanceMethod ev in actions)
			{
				try
				{
					ev.Run(session, messageInfo);
				}
				catch (Exception e)
				{
					Log.Error(e.ToString());
				}
			}
		}

		public override void Dispose()
		{
			if (this.Id == 0)
			{
				return;
			}

			base.Dispose();
		}
	}
}