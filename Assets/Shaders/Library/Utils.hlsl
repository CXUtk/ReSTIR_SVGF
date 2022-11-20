#pragma once

float Luminance(float3 color)
{
    return abs(dot(float3(0.2126, 0.7152, 0.0722), color));
}