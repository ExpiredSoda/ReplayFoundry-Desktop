import unittest
import json
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest.mock import patch
from replayfoundry_visual_semantic.recording_comparison import validate_regions, save_result


class RecordingComparisonTests(unittest.TestCase):
    def test_regions_keep_model_order_and_remove_duplicate_work(self):
        windows=[dict(ordinal=i) for i in range(10)]
        one=dict(firstWindow=6,lastWindow=8,reason="An event reaches its outcome.")
        two=dict(firstWindow=1,lastWindow=2,reason="A different event stands out.")
        self.assertEqual([one,two],validate_regions(dict(regions=[one,one,two]),windows,3))

    def test_unavailable_or_unbounded_regions_are_rejected(self):
        windows=[dict(ordinal=i) for i in range(10)]
        for first,last in ((-1,1),(3,2),(6,10),(True,2),(0,4)):
            with self.subTest(first=first,last=last),self.assertRaises(ValueError):
                validate_regions(dict(regions=[dict(firstWindow=first,lastWindow=last,reason="A proposed event.")]),windows,8)

    def test_new_cache_directory_is_created_and_result_remains_available(self):
        with TemporaryDirectory() as directory:
            root=Path(directory)
            result=dict(regions=[dict(firstWindow=1,lastWindow=2,reason="A developing event.")])
            save_result(root/'output.json',root/'new-cache'/'entry.json','recording-key',result)
            self.assertEqual(result['regions'],json.loads((root/'output.json').read_text())['regions'])
            self.assertEqual('recording-key',json.loads((root/'new-cache'/'entry.json').read_text())['key'])
            self.assertTrue(json.loads((root/'output.json').read_text())['cacheSaved'])

    def test_unwritable_cache_does_not_discard_completed_model_work(self):
        from replayfoundry_visual_semantic.recording_index import write_atomic
        with TemporaryDirectory() as directory:
            root=Path(directory)
            result=dict(regions=[dict(firstWindow=1,lastWindow=2,reason="A developing event.")])
            cache=root/'entry.json'
            def write(path,value):
                if path == cache: raise PermissionError('Cache unavailable')
                write_atomic(path,value)
            with patch('replayfoundry_visual_semantic.recording_comparison.write_atomic',side_effect=write):
                save_result(root/'output.json',cache,'recording-key',result)
            output=json.loads((root/'output.json').read_text())
            self.assertEqual(result['regions'],output['regions'])
            self.assertFalse(output['cacheSaved'])
