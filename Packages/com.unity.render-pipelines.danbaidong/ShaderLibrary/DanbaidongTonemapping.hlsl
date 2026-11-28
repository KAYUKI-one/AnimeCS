#ifndef DANBAIDONG_TONEMAPPING_INCLUDED
#define DANBAIDONG_TONEMAPPING_INCLUDED

// From DanbaidongRPCore f4c1e9ea.
float3 AcesFilm(float3 x)
{
    return saturate(x * (1.36 * x + 0.047) / (x * (0.93 * x + 0.56) + 0.14));
}

float GranTurismoTonemap(float x, float peak, float contrast, float start, float length, float toePower, float black)
{
    float linearLength = (peak - start) * length / contrast;
    float shoulderStart = start + linearLength;
    float shoulderValue = start + contrast * linearLength;
    float shoulderRate = contrast / (peak - shoulderValue);
    float toe = start * pow(x / start, toePower) + black;
    float linearValue = start + contrast * (x - start);
    float shoulder = peak - (peak - shoulderValue) * exp(-shoulderRate * (x - shoulderStart));
    float toeWeight = 1 - smoothstep(0, start, x);
    float shoulderWeight = step(shoulderStart, x);
    return toe * toeWeight + linearValue * (1 - toeWeight - shoulderWeight) + shoulder * shoulderWeight;
}

#endif
