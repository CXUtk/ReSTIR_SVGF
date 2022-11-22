﻿#pragma once
struct temporal_data
{
    float3 mean;
    float3 mean2;
    float3 meanShort;
    float count;
};

struct restir_sample
{
    float3 Xv, Nv;
    float3 Xs, Ns;
    float3 Lo;
};

struct restir_RESERVOIR
{
    restir_sample sample;
    float w, M, Wout;
};
static const int MAX = 20;
static const int MAX_SPATIAL = 300;
void RESERVOIR_update(inout restir_RESERVOIR R, restir_sample S, float W, float rand)
{
    R.w += W;
    R.M += 1;

    if(R.M > MAX)
    {
        R.w *= MAX / R.M;
        R.M = MAX;
    }
    if(rand < W / R.w)
    {
        R.sample = S;
    }
}

void RESERVOIR_merge(inout restir_RESERVOIR R, in restir_RESERVOIR R2, float p, float rand)
{
    float M0 = R.M;
    RESERVOIR_update(R, R2.sample, p * R2.Wout * R2.M, rand);
    R.M = min(MAX, M0 + R2.M);
}


void RESERVOIR_update_spataial(inout restir_RESERVOIR R, restir_sample S, float W, float rand)
{
    R.w += W;
    R.M += 1;

    if(R.M > MAX_SPATIAL)
    {
        R.w *= MAX_SPATIAL / R.M;
        R.M = MAX_SPATIAL;
    }
    if(rand < W / R.w)
    {
        R.sample = S;
    }
}


void RESERVOIR_merge_spataial(inout restir_RESERVOIR R, in restir_RESERVOIR R2, float p, float rand)
{
    float M0 = R.M;
    RESERVOIR_update_spataial(R, R2.sample, p * R2.Wout * R2.M, rand);
    R.M = min(MAX, M0 + R2.M);
}