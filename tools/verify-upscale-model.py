# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Condec contributors
"""
Checks that the ONNX model Condec bundles (tools/fetch-model.ps1) holds the official Real-ESRGAN x4plus weights.

  python tools/verify-upscale-model.py third_party/models/realesrgan-x4plus/model.onnx RealESRGAN_x4plus.pth

RealESRGAN_x4plus.pth is the file of the xinntao/Real-ESRGAN v0.1.0 release (BSD-3-Clause):
https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.0/RealESRGAN_x4plus.pth

It needs numpy, onnx, onnxruntime and torch (pip install numpy onnx onnxruntime torch). It is a one-off audit for whoever
changes the bundled model, not part of the build or the app. Result when it was written (2026-10-02): all 702 tensors of
the checkpoint are bit-identical in the ONNX, and torch and ONNX Runtime agree above 100 dB PSNR on three test inputs.

The Faithful model (tools/make-faithful-model.py) is checked the same way:

  python tools/verify-upscale-model.py third_party/models/realesrnet-x4plus/model.onnx third_party/checkpoints/RealESRNet_x4plus.pth

Result (2026-10-03): 702 of 702 tensors bit-identical, torch and ONNX Runtime agree at 127.7 to 130.0 dB PSNR.
"""
import sys

import numpy as np
import onnx
import onnxruntime as ort
import torch
import torch.nn as nn
import torch.nn.functional as F
from onnx import numpy_helper


# RRDBNet exactly as in basicsr / Real-ESRGAN (x4plus: 23 blocks, 64 features, 32 growth channels).
class ResidualDenseBlock(nn.Module):
    def __init__(self, num_feat=64, num_grow_ch=32):
        super().__init__()
        self.conv1 = nn.Conv2d(num_feat, num_grow_ch, 3, 1, 1)
        self.conv2 = nn.Conv2d(num_feat + num_grow_ch, num_grow_ch, 3, 1, 1)
        self.conv3 = nn.Conv2d(num_feat + 2 * num_grow_ch, num_grow_ch, 3, 1, 1)
        self.conv4 = nn.Conv2d(num_feat + 3 * num_grow_ch, num_grow_ch, 3, 1, 1)
        self.conv5 = nn.Conv2d(num_feat + 4 * num_grow_ch, num_feat, 3, 1, 1)
        self.lrelu = nn.LeakyReLU(negative_slope=0.2, inplace=True)

    def forward(self, x):
        x1 = self.lrelu(self.conv1(x))
        x2 = self.lrelu(self.conv2(torch.cat((x, x1), 1)))
        x3 = self.lrelu(self.conv3(torch.cat((x, x1, x2), 1)))
        x4 = self.lrelu(self.conv4(torch.cat((x, x1, x2, x3), 1)))
        x5 = self.conv5(torch.cat((x, x1, x2, x3, x4), 1))
        return x5 * 0.2 + x


class RRDB(nn.Module):
    def __init__(self, num_feat, num_grow_ch=32):
        super().__init__()
        self.rdb1 = ResidualDenseBlock(num_feat, num_grow_ch)
        self.rdb2 = ResidualDenseBlock(num_feat, num_grow_ch)
        self.rdb3 = ResidualDenseBlock(num_feat, num_grow_ch)

    def forward(self, x):
        out = self.rdb3(self.rdb2(self.rdb1(x)))
        return out * 0.2 + x


class RRDBNet(nn.Module):
    def __init__(self, num_in_ch=3, num_out_ch=3, num_feat=64, num_block=23, num_grow_ch=32):
        super().__init__()
        self.conv_first = nn.Conv2d(num_in_ch, num_feat, 3, 1, 1)
        self.body = nn.Sequential(*[RRDB(num_feat, num_grow_ch) for _ in range(num_block)])
        self.conv_body = nn.Conv2d(num_feat, num_feat, 3, 1, 1)
        self.conv_up1 = nn.Conv2d(num_feat, num_feat, 3, 1, 1)
        self.conv_up2 = nn.Conv2d(num_feat, num_feat, 3, 1, 1)
        self.conv_hr = nn.Conv2d(num_feat, num_feat, 3, 1, 1)
        self.conv_last = nn.Conv2d(num_feat, num_out_ch, 3, 1, 1)
        self.lrelu = nn.LeakyReLU(negative_slope=0.2, inplace=True)

    def forward(self, x):
        feat = self.conv_first(x)
        body_feat = self.conv_body(self.body(feat))
        feat = feat + body_feat
        feat = self.lrelu(self.conv_up1(F.interpolate(feat, scale_factor=2, mode='nearest')))
        feat = self.lrelu(self.conv_up2(F.interpolate(feat, scale_factor=2, mode='nearest')))
        return self.conv_last(self.lrelu(self.conv_hr(feat)))


def main(onnx_path, pth_path):
    checkpoint = torch.load(pth_path, map_location='cpu', weights_only=True)
    state = checkpoint['params_ema'] if 'params_ema' in checkpoint else checkpoint['params']
    net = RRDBNet()
    net.load_state_dict(state, strict=True)
    net.eval()

    # Is every checkpoint tensor in the ONNX, with exactly the same values?
    model = onnx.load(onnx_path)
    by_shape = {}
    for initializer in model.graph.initializer:
        array = numpy_helper.to_array(initializer)
        by_shape.setdefault(array.shape, []).append(array)
    missing = [name for name, tensor in state.items()
               if not any(np.array_equal(tensor.numpy(), a) for a in by_shape.get(tuple(tensor.shape), []))]
    print(f'checkpoint tensors found bit-identically in the ONNX: {len(state) - len(missing)} of {len(state)}')
    if missing:
        print('missing:', missing[:5])

    # Does the ONNX compute what the PyTorch network computes?
    rng = np.random.default_rng(5)
    ramp = np.linspace(0, 1, 80, dtype=np.float32)
    inputs = {
        'noise': rng.random((96, 96, 3), dtype=np.float32),
        'gradient': np.stack([np.tile(ramp, (64, 1)), np.tile(ramp[:64, None], (1, 80)), np.full((64, 80), 0.5, np.float32)], -1),
        'blocks': np.kron(rng.random((8, 8, 3), dtype=np.float32), np.ones((8, 8, 1), np.float32)),
    }
    session = ort.InferenceSession(onnx_path, providers=['CPUExecutionProvider'])
    name = session.get_inputs()[0].name
    for label, picture in inputs.items():
        x = np.transpose(picture, (2, 0, 1))[None].astype(np.float32)
        with torch.no_grad():
            reference = net(torch.from_numpy(x)).numpy()
        result = session.run(None, {name: x})[0]
        mse = float(np.mean((reference - result) ** 2))
        psnr = 10 * np.log10(1.0 / mse) if mse > 0 else float('inf')
        print(f'{label:9s} {picture.shape[1]}x{picture.shape[0]} -> {result.shape[3]}x{result.shape[2]}: PSNR torch vs ONNX {psnr:.1f} dB')

    return 0 if not missing else 1


if __name__ == '__main__':
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1], sys.argv[2]))
