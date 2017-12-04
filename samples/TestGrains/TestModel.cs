using System;

namespace TestGrains
{
	[Serializable]
	public class TestModel
	{
		public string Greeting { get; set; }

		public override string ToString()
		{
			return Greeting;
		}
	}
}