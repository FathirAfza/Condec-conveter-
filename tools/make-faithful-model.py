# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Condec contributors
"""
Makes the ONNX file of Real-ESRNet x4plus, the "Faithful" style of Upscale Image, from two pinned files:

- the ONNX export of Real-ESRGAN x4plus that Condec already bundles (tools/fetch-model.ps1), and
- RealESRNet_x4plus.pth from the xinntao/Real-ESRGAN v0.1.1 release (BSD-3-Clause).

Both are the same RRDBNet, with the same 702 tensors under the same names; only the weights differ. So the x4plus file is
copied and the bytes of every weight are overwritten in place with the ESRNet weights. Every other byte stays as it was,
which makes the result the same file on every machine (tools/fetch-model.ps1 pins its SHA-256).

  python tools/make-faithful-model.py <x4plus model.onnx> <RealESRNet_x4plus.pth> <output model.onnx>

It needs numpy and onnx (pip install numpy onnx). PyTorch is not needed: the checkpoint is read with an unpickler that
accepts nothing but float32 tensors in an ordered dictionary. tools/verify-upscale-model.py checks the result against the
checkpoint with PyTorch.
"""
import collections
import hashlib
import pickle
import sys
import zipfile

import numpy as np
import onnx
from onnx import numpy_helper


def read_checkpoint(path):
    """The tensors of a PyTorch zip checkpoint, as numpy arrays, without importing torch."""
    archive = zipfile.ZipFile(path)
    pickles = [name for name in archive.namelist() if name.endswith('/data.pkl')]
    if len(pickles) != 1:
        raise ValueError(f'{path} is not a PyTorch zip checkpoint')
    prefix = pickles[0][:-len('data.pkl')]

    def rebuild_tensor(storage, offset, size, stride, requires_grad, hooks, metadata=None):
        item = storage.itemsize
        return np.lib.stride_tricks.as_strided(storage[offset:], shape=tuple(size), strides=tuple(s * item for s in stride)).copy()

    class Unpickler(pickle.Unpickler):
        def find_class(self, module, name):
            allowed = {
                ('collections', 'OrderedDict'): collections.OrderedDict,
                ('torch._utils', '_rebuild_tensor_v2'): rebuild_tensor,
                ('torch', 'FloatStorage'): 'float32',
            }
            if (module, name) not in allowed:
                raise pickle.UnpicklingError(f'{module}.{name} is not expected in this checkpoint')
            return allowed[(module, name)]

        def persistent_load(self, pid):
            kind, storage_type, key, _location, count = pid
            if kind != 'storage' or storage_type != 'float32':
                raise pickle.UnpicklingError(f'unexpected storage {pid!r}')
            values = np.frombuffer(archive.read(f'{prefix}data/{key}'), dtype='<f4')
            if values.size != count:
                raise pickle.UnpicklingError(f'storage {key} holds {values.size} values, not {count}')
            return values

    with archive.open(pickles[0]) as stream:
        checkpoint = Unpickler(stream).load()
    # Like the official inference script: the averaged weights when the checkpoint has them.
    return checkpoint['params_ema'] if 'params_ema' in checkpoint else checkpoint['params']


def main(graph_path, checkpoint_path, output_path):
    weights = read_checkpoint(checkpoint_path)
    model = onnx.load(graph_path)
    data = bytearray(open(graph_path, 'rb').read())

    names = [initializer.name for initializer in model.graph.initializer]
    if sorted(names) != sorted(weights):
        raise ValueError('the checkpoint and the ONNX graph do not hold the same tensors')

    # Initializers are written in order, so each one's bytes are found after the previous one's.
    cursor = 0
    for initializer in model.graph.initializer:
        if initializer.data_type != onnx.TensorProto.FLOAT or not initializer.raw_data:
            raise ValueError(f'{initializer.name} is not stored as raw float32')
        new = np.ascontiguousarray(weights[initializer.name], dtype='<f4')
        if tuple(initializer.dims) != new.shape:
            raise ValueError(f'{initializer.name}: shape {tuple(initializer.dims)} in the graph, {new.shape} in the checkpoint')
        old = initializer.raw_data
        at = data.find(old, cursor)
        if at < 0:
            raise ValueError(f'the bytes of {initializer.name} were not found in {graph_path}')
        data[at:at + len(old)] = new.tobytes()
        cursor = at + len(old)

    # The result must be the graph unchanged, with exactly the checkpoint's weights.
    result = onnx.load_from_string(bytes(data))
    onnx.checker.check_model(result)
    for initializer in result.graph.initializer:
        if not np.array_equal(numpy_helper.to_array(initializer), weights[initializer.name]):
            raise ValueError(f'{initializer.name} did not come out as in the checkpoint')
    original, made = onnx.ModelProto(), onnx.ModelProto()
    original.CopyFrom(model)
    made.CopyFrom(result)
    del original.graph.initializer[:]
    del made.graph.initializer[:]
    if original.SerializeToString() != made.SerializeToString():
        raise ValueError('something other than the weights changed')

    with open(output_path, 'wb') as stream:
        stream.write(data)
    print(f'{len(names)} tensors replaced; {output_path} SHA-256 {hashlib.sha256(data).hexdigest()}')
    return 0


if __name__ == '__main__':
    if len(sys.argv) != 4:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1], sys.argv[2], sys.argv[3]))
