import json
from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from replayfoundry_visual_semantic.scene_cache import review_key,read_review,save_review


class SceneCacheTests(unittest.TestCase):
    def test_identity_tracks_model_prompts_pixels_speech_and_purpose(self):
        request={key:key for key in ('schemaVersion','modelHash','promptHash','factPromptHash','statesPromptHash','scorePromptHash')}
        case=dict(caseId='case',path='temporary-a.mp4',inputHash='pixels',start=0,end=10,transcript=[],candidateMode='StandaloneClip')
        expected=review_key(request,case,'attention')
        self.assertEqual(expected,review_key(request,{**case,'path':'temporary-b.mp4'},'attention'))
        for key in request:
            self.assertNotEqual(expected,review_key({**request,key:'changed'},case,'attention'))
        for change in ({'inputHash':'new pixels'},{'transcript':['corrected speech']},{'start':1},{'candidateMode':'MontageSegment'}):
            self.assertNotEqual(expected,review_key(request,{**case,**change},'attention'))
        self.assertNotEqual(expected,review_key(request,case,'changed attention'))

    def test_only_complete_intact_results_are_reused(self):
        with TemporaryDirectory() as directory:
            key='a'*64
            row=dict(status='Succeeded',caseId='case',assessment=dict(event='A supported event.'))
            save_review(directory,key,row)
            self.assertEqual(row,read_review(directory,key))
            path=Path(directory)/(key+'.scene.json')
            changed=json.loads(path.read_text()); changed['row']['assessment']['event']='An invented event.'
            path.write_text(json.dumps(changed))
            self.assertIsNone(read_review(directory,key))
            save_review(directory,'b'*64,dict(status='Failed'))
            self.assertFalse((Path(directory)/('b'*64+'.scene.json')).exists())
