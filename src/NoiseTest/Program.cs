// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");

int noiseComponent = 0;
int noiseFatigue = 0;

while (true)
{
    //Likelihood of increasing the noise component decreases with the absolute value of the noise (base is 50%)
    var noiseIncrease = Random.Shared.Next(Math.Abs(noiseComponent) + 0 + noiseFatigue / 30) == 0;
    if (noiseIncrease)
    {
        if (noiseComponent == 0)
        {
            noiseComponent += Random.Shared.Next(2) == 0 ? 1 : -1;
        }
        else
        {
            noiseComponent += Math.Sign(noiseComponent);
        }
    }
    else if (noiseComponent != 0)
    {
        noiseComponent -= Math.Sign(noiseComponent);
    }
    else
    {
        noiseFatigue = 0;
    }

    noiseFatigue += Math.Abs(noiseComponent);
    Console.WriteLine(noiseComponent);
    await Task.Delay(200);
}