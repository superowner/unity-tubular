﻿using System.Linq;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace Curve {

	public abstract class Curve {

		protected List<Vector3> points;
		protected bool closed;

		protected float[] cacheArcLengths;
		bool needsUpdate;

		public Curve(List<Vector3> points, bool closed = false) {
			this.points = points;
			this.closed = closed;
		}

		public abstract Vector3 GetPoint(float t);

		public virtual Vector3 GetTangent(float t) {
			var delta = 0.001f;
			var t1 = t - delta;
			var t2 = t + delta;

			// Capping in case of danger
			if (t1 < 0f) t1 = 0f;
			if (t2 > 1f) t2 = 1f;

			var pt1 = GetPoint(t1);
			var pt2 = GetPoint(t2);
			return (pt2 - pt1).normalized;
		}

		public Vector3 GetPointAt(float u) {
			float t = GetUtoTmapping(u);
			return GetPoint(t);
		}

		public Vector3 GetTangentAt(float u) {
			float t = GetUtoTmapping(u);
			return GetTangent(t);
		}

		float[] GetLengths(int divisions = -1) {
			if (divisions < 0) {
				divisions = 200;
			}

			if (this.cacheArcLengths != null &&
				(this.cacheArcLengths.Length == divisions + 1) &&
				!this.needsUpdate) {
				return this.cacheArcLengths;
			}

			this.needsUpdate = false;

			var cache = new float[divisions + 1];
			Vector3 current, last = this.GetPoint(0f);

			cache[0] = 0f;

			float sum = 0f;
			for (int p = 1; p <= divisions; p ++ ) {
				current = this.GetPoint(1f * p / divisions);
				sum += Vector3.Distance(current, last);
				cache[p] = sum;
				last = current;
			}

			this.cacheArcLengths = cache;
			return cache;
		}

		// Given u ( 0 .. 1 ), get a t to find p. This gives you points which are equidistant
		protected float GetUtoTmapping(float u) {
			var arcLengths = this.GetLengths();

			int i = 0, il = arcLengths.Length;

			float targetArcLength; // The targeted u distance value to get
			/*
			if ( distance ) {
				targetArcLength = distance;
			} else {
				targetArcLength = u * arcLengths[il - 1];
			}
			*/
			targetArcLength = u * arcLengths[il - 1];

			// binary search for the index with largest value smaller than target u distance

			int low = 0, high = il - 1;
			float comparison;

			while ( low <= high ) {

				i = Mathf.FloorToInt(low + (high - low) / 2f); // less likely to overflow, though probably not issue here, JS doesn't really have integers, all numbers are floats

				comparison = arcLengths[i] - targetArcLength;

				if (comparison < 0f) {

					low = i + 1;

				} else if (comparison > 0f) {

					high = i - 1;

				} else {

					high = i;
					break;

					// DONE

				}

			}

			i = high;

			if (Mathf.Approximately(arcLengths[i], targetArcLength)) {

				return 1f * i / ( il - 1 );

			}

			// we could get finer grain at lengths, or use simple interpolation between two points

			var lengthBefore = arcLengths[i];
			var lengthAfter = arcLengths[i + 1];

			var segmentLength = lengthAfter - lengthBefore;

			// determine where we are between the 'before' and 'after' points

			var segmentFraction = ( targetArcLength - lengthBefore ) / segmentLength;

			// add that fractional amount to t
			var t = 1f * (i + segmentFraction) / (il - 1);

			return t;
		}

		// https://github.com/neilmendoza/ofxPtf
		/*
		public List<FrenetFrame> ComputeParallelTransportFrames (int segments, bool closed) {
			var ptf = new ParallelTransportFrame();
			var frames = ptf.Build(Curve, segments);
			// var frames = new List<FrenetFrame>();

			return frames;
		}
		*/

		public List<FrenetFrame> ComputeFrenetFrames (int segments, bool closed) {
			// see http://www.cs.indiana.edu/pub/techreports/TR425.pdf
			var normal = new Vector3();

			var tangents = new Vector3[segments + 1];
			var normals = new Vector3[segments + 1];
			var binormals = new Vector3[segments + 1];

			var vec = new Vector3();
			var mat = new Matrix4x4();

			float u, theta;

			// compute the tangent vectors for each segment on the curve
			for (int i = 0; i <= segments; i++) {
				u = (1f * i) / segments;
				tangents[i] = GetTangentAt(u).normalized;
			}

			// select an initial normal vector perpendicular to the first tangent vector,
			// and in the direction of the minimum tangent xyz component

			normals[0] = new Vector3();
			binormals[0] = new Vector3();

			var min = float.MaxValue;
			var tx = Mathf.Abs(tangents[0].x);
			var ty = Mathf.Abs(tangents[0].y);
			var tz = Mathf.Abs(tangents[0].z);
			if (tx <= min) {
				min = tx;
				normal.Set(1, 0, 0);
			}
			if (ty <= min) {
				min = ty;
				normal.Set(0, 1, 0);
			}
			if (tz <= min) {
				normal.Set(0, 0, 1);
			}

			vec = Vector3.Cross(tangents[0], normal).normalized;

			normals[0] = Vector3.Cross(tangents[0], vec);
			binormals[0] = Vector3.Cross(tangents[0], normals[0]);

			// compute the slowly-varying normal and binormal vectors for each segment on the curve

			for (int i = 1; i <= segments; i++) {
				// copy previous
				normals[i] = normals[i - 1];
				binormals[i] = binormals[i - 1];

				// Rotation axis
				vec = Vector3.Cross(tangents[i - 1], tangents[i]);

				if (vec.magnitude > float.Epsilon) {
					vec.Normalize();

					float dot = Vector3.Dot(tangents[i - 1], tangents[i]);

					// clamp for floating pt errors
					theta = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));

					normals[i] = Quaternion.AngleAxis(theta * Mathf.Rad2Deg, vec) * normals[i];
				}

				binormals[i] = Vector3.Cross(tangents[i], normals[i]).normalized;
			}

			// if the curve is closed, postprocess the vectors so the first and last normal vectors are the same

			if (closed) {
				theta = Mathf.Acos(Mathf.Clamp(Vector3.Dot(normals[0], normals[segments]), -1f, 1f));
				theta /= segments;

				// if (tangents[ 0 ].dot( vec.crossVectors( normals[ 0 ], normals[ segments ] ) ) > 0 ) {
				if (Vector3.Dot(tangents[0], Vector3.Cross(normals[0], normals[segments])) > 0f) {
					theta = - theta;
				}

				for (int i = 1; i <= segments; i++) {
					// twist a little...
					// normals[ i ].applyMatrix4( mat.makeRotationAxis( tangents[ i ], theta * i ) );
					normals[i] = (Quaternion.AngleAxis(Mathf.Deg2Rad * theta * i, tangents[i]) * normals[i]);
					binormals[i] = Vector3.Cross(tangents[i], normals[i]);
				}

			}

			var frames = new List<FrenetFrame>();

			int n = tangents.Length;
			for(int i = 0; i < n; i++) {
				var frame = new FrenetFrame(tangents[i], normals[i], binormals[i]);
				frames.Add(frame);
			}

			return frames;
		}


	}
		
}
