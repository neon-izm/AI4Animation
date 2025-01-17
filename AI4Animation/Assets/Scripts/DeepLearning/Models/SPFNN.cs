﻿using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DeepLearning {

	public class SPFNN : Model {

		public int XDim = 0;
		public int HDim = 0;
		public int YDim = 0;

		public int SDim = 0;
		public int[] StyleNeurons = new int[0];

		public int PhaseIndex = 0;

		private Tensor Xmean, Xstd, Ymean, Ystd;
		private Tensor[] W0, W1, W2, b0, b1, b2;
		private Tensor[] SW, Sb;
		private Tensor X, Y;

		private Tensor WS, bS;

		private float Phase;
		private float Damping;

		private const float M_PI = 3.14159265358979323846f;

		public SPFNN() {
			
		}

		public override void StoreParameters() {
			Parameters = ScriptableObject.CreateInstance<Parameters>();
			Parameters.Store(Folder+"/Xmean.bin", XDim, 1);
			Parameters.Store(Folder+"/Xstd.bin", XDim, 1);
			Parameters.Store(Folder+"/Ymean.bin", YDim, 1);
			Parameters.Store(Folder+"/Ystd.bin", YDim, 1);
			for(int i=0; i<50; i++) {
				Parameters.Store(Folder+"/W0_"+i.ToString("D3")+".bin", HDim, XDim);
				Parameters.Store(Folder+"/W1_"+i.ToString("D3")+".bin", HDim, HDim);
				Parameters.Store(Folder+"/W2_"+i.ToString("D3")+".bin", YDim, HDim);
				Parameters.Store(Folder+"/b0_"+i.ToString("D3")+".bin", HDim, 1);
				Parameters.Store(Folder+"/b1_"+i.ToString("D3")+".bin", HDim, 1);
				Parameters.Store(Folder+"/b2_"+i.ToString("D3")+".bin", YDim, 1);
			}
			for(int i=0; i<SDim; i++) {
				Parameters.Store(Folder+"/cp3_a"+i.ToString()+".bin", YDim, YDim);
				Parameters.Store(Folder+"/cp3_b"+i.ToString()+".bin", YDim, 1);
			}
		}

		public override void LoadParameters() {
			if(Parameters == null) {
				Debug.Log("Building PFNN failed because no parameters were saved.");
				return;
			}

			Xmean = CreateTensor(Parameters.Load(0), "Xmean");
			Xstd = CreateTensor(Parameters.Load(1), "Xstd");
			Ymean = CreateTensor(Parameters.Load(2), "Ymean");
			Ystd = CreateTensor(Parameters.Load(3), "Ystd");

			W0 = new Tensor[50];
			W1 = new Tensor[50];
			W2 = new Tensor[50];
			b0 = new Tensor[50];
			b1 = new Tensor[50];
			b2 = new Tensor[50];
			for(int i=0; i<50; i++) {
				W0[i] = CreateTensor(Parameters.Load(4 + i*6 + 0), "W0"+i);
				W1[i] = CreateTensor(Parameters.Load(4 + i*6 + 1), "W1"+i);
				W2[i] = CreateTensor(Parameters.Load(4 + i*6 + 2), "W2"+i);
				b0[i] = CreateTensor(Parameters.Load(4 + i*6 + 3), "b0"+i);
				b1[i] = CreateTensor(Parameters.Load(4 + i*6 + 4), "b1"+i);
				b2[i] = CreateTensor(Parameters.Load(4 + i*6 + 5), "b2"+i);
			}
			SW = new Tensor[SDim];
			Sb = new Tensor[SDim];
			for(int i=0; i<SDim; i++) {
				SW[i] = CreateTensor(Parameters.Load(4 + 50*6 + i*2 + 0), "SW"+i.ToString());
				Sb[i] = CreateTensor(Parameters.Load(4 + 50*6 + i*2 + 1), "Sb"+i.ToString());
			}
			WS = CreateTensor(YDim, YDim, "WS");
			bS = CreateTensor(YDim, 1, "bS");

			X = CreateTensor(XDim, 1, "X");
			Y = CreateTensor(YDim, 1, "Y");

			Phase = 0f;
			Damping = 0f;
		}

		public override void SetInput(int index, float value) {
			X.SetValue(index, 0, value);
		}

		public override float GetOutput(int index) {
			return Y.GetValue(index, 0);
		}
		
		public override void Predict() {
			float[] S = new float[SDim];
			for(int i=0; i<SDim; i++) {
				S[i] = X.GetValue(StyleNeurons[i], 0);
			}

			//Normalise Input
			Normalise(X, Xmean, Xstd, Y);

			//Process PFNN-Network
			int index = (int)((Phase / (2f*M_PI)) * 50f);
			ELU(Layer(Y, W0[index], b0[index], Y));
			ELU(Layer(Y, W1[index], b1[index], Y));
			ELU(Layer(Y, W2[index], b2[index], Y));

			//Process S-Layer
			WS.SetZero();
			bS.SetZero();
			for(int i=0; i<SDim; i++) {
				Blend(WS, SW[i], S[i]);
				Blend(bS, Sb[i], S[i]);
			}
			Layer(Y, WS, bS, Y);

			//Renormalise Output
			Renormalise(Y, Ymean, Ystd, Y);

			//Update Phase
			Phase = Mathf.Repeat(Phase + (1f-Damping)*GetOutput(PhaseIndex)*2f*Mathf.PI, 2f*Mathf.PI);
		}

		/*
		private Matrix Linear(ref Matrix y0, ref Matrix y1, float mu) {
			return (1.0f-mu) * y0 + (mu) * y1;
		}

		private Matrix Cubic(ref Matrix y0, ref Matrix y1, ref Matrix y2, ref Matrix y3, float mu) {
			return
			(-0.5f*y0 + 1.5f*y1 - 1.5f*y2 + 0.5f*y3)*mu*mu*mu + 
			(y0 - 2.5f*y1 + 2.0f*y2 - 0.5f*y3)*mu*mu + 
			(-0.5f*y0 + 0.5f*y2)*mu + 
			(y1);
		}
		*/

		public void SetDamping(float value) {
			Damping = value;
		}

		public float GetPhase() {
			return Phase;
		}

		#if UNITY_EDITOR
		public override void Inspector() {
			using(new EditorGUILayout.VerticalScope ("Box")) {
				Folder = EditorGUILayout.TextField("Folder", Folder);
				EditorGUILayout.BeginHorizontal();
				if(GUILayout.Button("Store Parameters")) {
					StoreParameters();
				}
				Parameters = (Parameters)EditorGUILayout.ObjectField(Parameters, typeof(Parameters), true);
				EditorGUILayout.EndHorizontal();

				XDim = EditorGUILayout.IntField("XDim", XDim);
				HDim = EditorGUILayout.IntField("HDim", HDim);
				YDim = EditorGUILayout.IntField("YDim", YDim);
				SDim = EditorGUILayout.IntField("SDim", SDim);
				Array.Resize(ref StyleNeurons, EditorGUILayout.IntField("Style Neurons", StyleNeurons.Length));
				for(int i=0; i<StyleNeurons.Length; i++) {
					StyleNeurons[i] = EditorGUILayout.IntField("Neuron " + (i+1), StyleNeurons[i]);
				}
				PhaseIndex = EditorGUILayout.IntField("Phase Index", PhaseIndex);

				EditorGUILayout.Slider("Phase", Phase, 0f, 2f*Mathf.PI);
			}
		}
		#endif

	}

}