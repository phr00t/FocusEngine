// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

#include "../../../deps/NativePath/NativePath.h"
#include "../../Xenko.Native/XenkoNative.h"
#define HAVE_STDINT_H
#include "../../../../deps/Celt/include/opus_custom.h"
#include <stdio.h>

extern "C" {
	class XenkoCelt
	{
	public:
		XenkoCelt(int sampleRate, int bufferSize, int channels, bool decoderOnly);

		~XenkoCelt();

		bool Init(char* err_str);

		OpusCustomEncoder* GetEncoder() const;

		OpusCustomDecoder* GetDecoder() const;

	private:
		OpusCustomMode* mode_;
		OpusCustomDecoder* decoder_;
		OpusCustomEncoder* encoder_;
		int sample_rate_;
		int buffer_size_;
		int channels_;
		bool decoder_only_;
	};

	DLL_EXPORT_API void* xnCeltCreate(int sampleRate, int bufferSize, int channels, bool decoderOnly, char* error)
	{
		XenkoCelt* celt = new XenkoCelt(sampleRate, bufferSize, channels, decoderOnly);
		if(!celt->Init(error))
		{
			delete celt;
			return nullptr;
		}
		return celt;
	}

	DLL_EXPORT_API void xnCeltDestroy(XenkoCelt* celt)
	{
		delete celt;
	}

	DLL_EXPORT_API void xnCeltResetDecoder(XenkoCelt* celt)
	{
		opus_custom_decoder_ctl(celt->GetDecoder(), OPUS_RESET_STATE);
	}

	DLL_EXPORT_API int xnCeltGetDecoderSampleDelay(XenkoCelt* celt, int32_t* delay)
	{
		return opus_custom_decoder_ctl(celt->GetDecoder(), OPUS_GET_LOOKAHEAD(delay));
	}

	DLL_EXPORT_API int xnCeltEncodeFloat(XenkoCelt* celt, float* inputSamples, int numberOfInputSamples, uint8_t* outputBuffer, int maxOutputSize)
	{
		return opus_custom_encode_float(celt->GetEncoder(), inputSamples, numberOfInputSamples, outputBuffer, maxOutputSize);
	}

	DLL_EXPORT_API int xnCeltDecodeFloat(XenkoCelt* celt, uint8_t* inputBuffer, int inputBufferSize, float* outputBuffer, int numberOfOutputSamples)
	{
		return opus_custom_decode_float(celt->GetDecoder(), inputBuffer, inputBufferSize, outputBuffer, numberOfOutputSamples);
	}

	DLL_EXPORT_API int xnCeltEncodeShort(XenkoCelt* celt, int16_t* inputSamples, int numberOfInputSamples, uint8_t* outputBuffer, int maxOutputSize)
	{
		return opus_custom_encode(celt->GetEncoder(), inputSamples, numberOfInputSamples, outputBuffer, maxOutputSize);
	}

	DLL_EXPORT_API int xnCeltDecodeShort(XenkoCelt* celt, uint8_t* inputBuffer, int inputBufferSize, int16_t* outputBuffer, int numberOfOutputSamples)
	{
		return opus_custom_decode(celt->GetDecoder(), inputBuffer, inputBufferSize, outputBuffer, numberOfOutputSamples);
	}
}

XenkoCelt::XenkoCelt(int sampleRate, int bufferSize, int channels, bool decoderOnly): mode_(nullptr), decoder_(nullptr), encoder_(nullptr), sample_rate_(sampleRate), buffer_size_(bufferSize), channels_(channels), decoder_only_(decoderOnly)
{
}

XenkoCelt::~XenkoCelt()
{
	if (encoder_) opus_custom_encoder_destroy(encoder_);
	encoder_ = nullptr;
	if (decoder_) opus_custom_decoder_destroy(decoder_);
	decoder_ = nullptr;
	if (mode_) opus_custom_mode_destroy(mode_);
	mode_ = nullptr;
}

bool XenkoCelt::Init(char* err_str)
{
	int err;

	mode_ = opus_custom_mode_create(sample_rate_, buffer_size_, &err);
	if (!mode_) {
		sprintf(err_str, "opus_custom_mode_create_err: %d", err);
		return false;
	}

	decoder_ = opus_custom_decoder_create(mode_, channels_, &err);
	if (!decoder_) {
		sprintf(err_str, "opus_custom_decoder_create: %d", err);
		return false;
	}

	if (!decoder_only_)
	{
		encoder_ = opus_custom_encoder_create(mode_, channels_, &err);
		if (!encoder_) {
			sprintf(err_str, "opus_custom_encoder_create: %d", err);
			return false;
		}
	}

	return true;
}

OpusCustomEncoder* XenkoCelt::GetEncoder() const
{
	return encoder_;
}

OpusCustomDecoder* XenkoCelt::GetDecoder() const
{
	return decoder_;
}
