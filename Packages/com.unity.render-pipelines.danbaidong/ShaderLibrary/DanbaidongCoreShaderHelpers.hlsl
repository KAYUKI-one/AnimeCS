#ifndef DANBAIDONG_CORE_SHADER_HELPERS_INCLUDED
#define DANBAIDONG_CORE_SHADER_HELPERS_INCLUDED
// Adapted from DanbaidongRPCore f4c1e9ea, see the package license.
#ifndef GGX_BOUNDED_VNDF_SAMPLING
#define GGX_BOUNDED_VNDF_SAMPLING 1
#endif

float SigmoidSharp(float x, float center, float sharp) {
    float s;
    s = 1.0 / (1.0 + pow(100000.0, (-3.0 * sharp * (x - center))));
    return s;
}

float VisibleGGXPDF(float3 V, float3 H, float a2, bool bLimitVDNFToReflection = true)
{
	float NoV = V.z;
	float NoH = H.z;
	float VoH = dot(V, H);

	float d = (NoH * a2 - NoH) * NoH + 1;
	float D = a2 / (PI*d*d);
	float k = 1.0;
#if GGX_BOUNDED_VNDF_SAMPLING
	if (bLimitVDNFToReflection)
	{
		float s = 1.0f + length(V.xy);
		float s2 = s * s;
		k = (s2 - a2 * s2) / (s2 + a2 * V.z * V.z); // Eq. 5
	}
#endif
	float PDF = 2 * VoH * D / (k * NoV + sqrt(NoV * (NoV - NoV * a2) + a2));
	return PDF;
}

void SampleAnisoGGXVisibleNormalSphericalCaps(float2 u,
                                 float3 V,
                                 float3x3 localToWorld,
                                 float roughnessX,
                                 float roughnessY,
                             out float3 localV,
                             out float3 localH,
                             out float  VdotH,
                                 bool bLimitVDNFToReflection)
{
    localV = mul(V, transpose(localToWorld));

    // Stretch view direction, Hemisphere Space
    float3 Vh = normalize(float3(roughnessX * localV.x, roughnessY * localV.y, localV.z));
    float Phi = (2 * PI) * u.x;
    float k = 1.0;

#if GGX_BOUNDED_VNDF_SAMPLING
	if (bLimitVDNFToReflection)
	{
		// If we know we will be reflecting the view vector around the sampled micronormal, we can
		// tweak the range a bit more to eliminate some of the vectors that will point below the horizon
		float a = saturate(min(roughnessX, roughnessY));
		float s = 1.0 + length(V.xy);
		float a2 = a * a, s2 = s * s;
		k = (s2 - a2 * s2) / (s2 + a2 * V.z * V.z);
	}
#endif

    float Z = lerp(1.0, -k * Vh.z, u.y);
    float SinTheta = sqrt(saturate(1 - Z * Z));
    float X = SinTheta * cos(Phi);
    float Y = SinTheta * sin(Phi);
    float3 H = float3(X, Y, Z) + Vh;

    // Transform the normal back to the Ellipsoid Space
    localH = normalize(float3(roughnessX * H.x, roughnessY * H.y, max(0.0, H.z)));

    VdotH = saturate(dot(localV, localH));
}

void SampleGGXVisibleNormalSphericalCaps(float2 u,
                                         float3 V,
                                         float3x3 localToWorld,
                                         float roughness,
                                     out float3 localV,
                                     out float3 localH,
                                     out float  VdotH,
                                         bool bLimitVDNFToReflection = true)
{
    SampleAnisoGGXVisibleNormalSphericalCaps(u, V, localToWorld, roughness, roughness, localV, localH, VdotH, bLimitVDNFToReflection);
}

#endif
