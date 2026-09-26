import unittest
from replayfoundry_visual_semantic.scene_value import relevance


class SceneValueTests(unittest.TestCase):
    def test_opposite_order_bias_cancels_without_erasing_disagreement(self):
        self.assertAlmostEqual(.5,relevance([8,-8])["value"])
        disputed=relevance([3.5,-1])
        self.assertGreater(disputed["value"],.5)
        self.assertLess(disputed["value"],.7)
        self.assertFalse(disputed["calibrated"])

    def test_score_is_continuous_symmetric_and_numerically_stable(self):
        self.assertLess(relevance([1,1])["value"],relevance([2,2])["value"])
        self.assertAlmostEqual(1,relevance([2,4])["value"]+relevance([-2,-4])["value"])
        self.assertEqual(1,relevance([10000,10000])["value"])
        self.assertEqual(0,relevance([-10000,-10000])["value"])
        for values in ([float('nan'),1],[float('inf'),0],[True,1],[1],[1,2,3]):
            with self.subTest(values=values),self.assertRaises(ValueError): relevance(values)
