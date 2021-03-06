﻿using MigAz.Core.Interface;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MigAz.Azure.Arm
{
    public class LoadBalancer : ArmResource, ILoadBalancer
    {
        public LoadBalancer(JToken resourceToken) : base(resourceToken)
        {
        }

        public override string ToString()
        {
            return this.Name;
        }
    }
}
